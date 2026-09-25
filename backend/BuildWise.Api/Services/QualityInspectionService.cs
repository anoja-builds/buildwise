using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class QualityInspectionService
{
    private readonly ApplicationDbContext _context;
    private readonly OperationalAgentClient? _agentClient;
    private readonly OperationalAgentAuditService? _auditService;
    private readonly NotificationService? _notificationService;

    public QualityInspectionService(
        ApplicationDbContext context,
        OperationalAgentClient? agentClient = null,
        OperationalAgentAuditService? auditService = null,
        NotificationService? notificationService = null)
    {
        _context = context;
        _agentClient = agentClient;
        _auditService = auditService;
        _notificationService = notificationService;
    }

    public async Task<Inspection> CompleteInspectionAsync(CompleteInspectionDto dto, int inspectorUserId)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("An inspection must contain at least one material line.");
        if (dto.Evidence?.Count > 10)
            throw new InvalidOperationException("An inspection can contain at most 10 evidence records.");
        if (dto.Evidence?.Any(e => string.IsNullOrWhiteSpace(e.FileName) || string.IsNullOrWhiteSpace(e.FileUrl) || !Uri.TryCreate(e.FileUrl, UriKind.Absolute, out _)) == true)
            throw new InvalidOperationException("Each evidence record requires a file name and an absolute URL.");
        if (dto.Evidence?.Any(e => e.FileSizeBytes is < 0 or > 10_000_000) == true)
            throw new InvalidOperationException("Evidence files must be between 0 bytes and 10 MB.");

        if (dto.Items.Any(i => i.RejectedQuantity > 0 && string.IsNullOrWhiteSpace(i.RejectionReason)))
            throw new InvalidOperationException("A rejection reason is required for every rejected material line.");
        if (dto.Items.Any(i => i.InspectedQuantity <= 0 || i.AcceptedQuantity < 0 || i.RejectedQuantity < 0 || i.AcceptedQuantity + i.RejectedQuantity != i.InspectedQuantity))
            throw new InvalidOperationException("Inspection quantities must be non-negative and accepted plus rejected must equal inspected quantity.");

        return await CompleteInspectionAsync(new Inspection
        {
            DeliveryId = dto.DeliveryId,
            InspectorUserId = inspectorUserId,
            InspectionCriteria = dto.InspectionCriteria,
            ObservedResult = dto.ObservedResult,
            Notes = dto.Notes,
            Evidence = (dto.Evidence ?? new()).Select(e => new InspectionEvidence
            {
                FileName = e.FileName.Trim(), FileUrl = e.FileUrl.Trim(), ContentType = e.ContentType,
                FileSizeBytes = e.FileSizeBytes, UploadedByUserId = inspectorUserId, UploadedAt = DateTime.UtcNow
            }).ToList(),
            Items = dto.Items.Select(i => new InspectionItem
            {
                MaterialId = i.MaterialId, InspectedQuantity = i.InspectedQuantity,
                AcceptedQuantity = i.AcceptedQuantity, RejectedQuantity = i.RejectedQuantity,
                RejectionReason = i.RejectionReason?.Trim() ?? string.Empty
            }).ToList()
        });
    }

    /// <summary>
    /// Non-Conformance Reports (NCRs) for any rejected materials.
    /// </summary>
    public async Task<Inspection> CompleteInspectionAsync(Inspection inspection)
    {
        // Rule 1: Delivery must exist
        var delivery = await _context.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Quotation)
                    .ThenInclude(q => q!.MaterialRequest)
            .FirstOrDefaultAsync(d => d.Id == inspection.DeliveryId);
        if (delivery == null)
            throw new KeyNotFoundException("Delivery record not found.");

        // Rule 2: Quantity Arithmetic Validation
        bool hasRejections = false;
        foreach (var item in inspection.Items)
        {
            if (item.AcceptedQuantity < 0 || item.RejectedQuantity < 0 || item.InspectedQuantity < 0)
            {
                throw new InvalidOperationException(
                    $"Inspected, accepted and rejected quantities cannot be negative for material {item.MaterialId}.");
            }

            if (item.AcceptedQuantity + item.RejectedQuantity != item.InspectedQuantity)
            {
                throw new InvalidOperationException(
                    $"Accepted ({item.AcceptedQuantity}) + Rejected ({item.RejectedQuantity}) " +
                    $"must equal Inspected quantity ({item.InspectedQuantity}) for material {item.MaterialId}.");
            }

            if (item.RejectedQuantity > 0)
            {
                if (string.IsNullOrWhiteSpace(item.RejectionReason))
                    throw new InvalidOperationException($"A rejection reason is required for material {item.MaterialId}.");
                hasRejections = true;
            }
        }

        var materialIds = inspection.Items.Select(item => item.MaterialId).Distinct().ToList();
        var materials = await _context.Materials
            .Where(material => materialIds.Contains(material.Id))
            .ToDictionaryAsync(material => material.Id);
        var qualityItems = inspection.Items.Select(item =>
        {
            materials.TryGetValue(item.MaterialId, out var material);
            return new QualityAgentItem(
                material?.Name ?? $"Material #{item.MaterialId}",
                item.InspectedQuantity,
                item.RejectedQuantity,
                item.AcceptedQuantity,
                item.RejectionReason ?? string.Empty);
        }).ToList();

        var agentRun = _agentClient == null
            ? new AgentClientResult<QualityAgentResult>(
                QualityAgentResult.Deterministic(qualityItems),
                "DeterministicFallback")
            : await _agentClient.AnalyzeQualityRiskAsync(inspection.DeliveryId, qualityItems);
        var qualityAssessment = agentRun.Output;

        inspection.Status = InspectionStatus.Completed;
        inspection.OverallDecision = hasRejections ? InspectionDecision.PartiallyAccepted : InspectionDecision.Accepted;
        inspection.InspectedAt = DateTime.UtcNow;
        inspection.UpdatedAt = DateTime.UtcNow;

        _context.Inspections.Add(inspection);
        // Phase 1: persist the inspection and its items first so relational
        // providers assign database-generated item IDs (the in-memory test
        // provider assigns keys at Add-time, which masks this ordering).
        await _context.SaveChangesAsync();

        // Rule 3: Automatically generate Non-Conformance Report (NCR) for rejected materials
        var reservedNcrNumbers = new HashSet<string>(StringComparer.Ordinal);
        var generatedNcrCount = 0;
        foreach (var item in inspection.Items.Where(i => i.RejectedQuantity > 0))
        {
            var ncr = new NonConformance
            {
                NcrNumber = await GenerateUniqueNcrNumberAsync(reservedNcrNumbers),
                InspectionItemId = item.Id,
                DeliveryId = inspection.DeliveryId,
                MaterialId = item.MaterialId,
                SupplierId = delivery.PurchaseOrder?.SupplierId,
                QuantityAffected = item.RejectedQuantity,
                Severity = Enum.TryParse<NonConformanceSeverity>(qualityAssessment.RiskLevel, true, out var severity)
                    ? severity
                    : NonConformanceSeverity.Medium,
                Status = NonConformanceStatus.CorrectiveActionRequired,
                IssueDescription = string.IsNullOrWhiteSpace(item.RejectionReason)
                    ? $"Quality failure: {item.RejectedQuantity} units rejected during site inspection."
                    : item.RejectionReason,
                CorrectiveActionPlan = qualityAssessment.SuggestedCorrectiveAction,
                ResponsibleUserId = inspection.InspectorUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.NonConformances.Add(ncr);
            generatedNcrCount++;
        }

        // Phase 2: NCRs now reference persisted inspection-item IDs.
        await _context.SaveChangesAsync();

        if (_notificationService != null)
        {
            var requesterId = delivery.PurchaseOrder?.Quotation?.MaterialRequest?.RequestedByUserId;
            if (requesterId.HasValue)
            {
                var latestNcrId = generatedNcrCount > 0
                    ? await _context.NonConformances.Where(n => n.InspectionItemId == inspection.Items[0].Id).OrderByDescending(n => n.Id).Select(n => (int?)n.Id).FirstOrDefaultAsync()
                    : null;
                await _notificationService.CreateInspectionEventAsync(requesterId.Value, inspection.Id, inspection.DeliveryId, latestNcrId ?? 0, inspection.OverallDecision.ToString());
            }
        }

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(
                inspection.InspectorUserId,
                $"Assess quality risk for delivery #{inspection.DeliveryId} and determine NCR corrective action.",
                "QualityRiskAnalysisAgent",
                new[] { "Read validated inspection lines", "Call analyze_quality_risk", "Re-check arithmetic and mandatory-NCR rule" },
                new[] { "analyze_quality_risk" },
                qualityAssessment,
                agentRun.ExecutionSource,
                new
                {
                    valid = inspection.Items.All(i => i.AcceptedQuantity + i.RejectedQuantity == i.InspectedQuantity),
                    ncrRequiredForRejectedItems = hasRejections,
                    businessRule = "rejected>0 => NCR"
                },
                deliveryId: inspection.DeliveryId);
        }

        return inspection;
    }

    /// <summary>
    /// Allocates the next NCR number. Keeps the documented "NCR-######" shape
    /// (six trailing digits derived from the UTC tick count) while guaranteeing
    /// the value is unique both within the current batch and against already
    /// persisted reports — the database enforces a unique index on this column.
    /// </summary>
    private async Task<string> GenerateUniqueNcrNumberAsync(ISet<string> reservedInThisBatch)
    {
        var ticks = DateTime.UtcNow.Ticks;
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var digits = (ticks + attempt).ToString();
            var candidate = $"NCR-{digits[^6..]}";

            if (reservedInThisBatch.Contains(candidate))
                continue;

            if (await _context.NonConformances.AnyAsync(n => n.NcrNumber == candidate))
                continue;

            reservedInThisBatch.Add(candidate);
            return candidate;
        }

        throw new InvalidOperationException("Unable to allocate a unique NCR number.");
    }

    /// <summary>
    /// Retrieves all open (non-closed) non-conformances with related inspection item and material data.
    /// </summary>
    public async Task<List<NonConformance>> GetOpenNonConformancesAsync()
    {
        return await _context.NonConformances
            .Include(n => n.InspectionItem)
            .ThenInclude(i => i!.Material)
            .Include(n => n.InspectionItem)
                .ThenInclude(i => i!.Inspection)
                    .ThenInclude(i => i!.Delivery)
            .Where(n => n.Status != NonConformanceStatus.Closed)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Retrieves a specific non-conformance by ID.
    /// </summary>
    public async Task<NonConformance?> GetNonConformanceByIdAsync(int id)
    {
        return await _context.NonConformances
            .Include(n => n.InspectionItem)
            .ThenInclude(i => i!.Material)
            .FirstOrDefaultAsync(n => n.Id == id);
    }

    /// <summary>
    /// Updates the status of a non-conformance (e.g., to Resolved or Closed).
    /// </summary>
    public async Task<List<Inspection>> GetInspectionsAsync(int? deliveryId = null, InspectionStatus? status = null)
    {
        var query = _context.Inspections
            .Include(i => i.Delivery)
                .ThenInclude(d => d!.PurchaseOrder)
                    .ThenInclude(po => po!.Supplier)
            .Include(i => i.Items)
                .ThenInclude(i => i.Material)
            .Include(i => i.Evidence)
            .AsQueryable();
        if (deliveryId.HasValue) query = query.Where(i => i.DeliveryId == deliveryId.Value);
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        return await query.OrderByDescending(i => i.InspectedAt).ToListAsync();
    }

    public async Task<Inspection?> GetInspectionByIdAsync(int id)
    {
        return await _context.Inspections
            .Include(i => i.Delivery)
                .ThenInclude(d => d!.PurchaseOrder)
                    .ThenInclude(po => po!.Supplier)
            .Include(i => i.Items)
                .ThenInclude(i => i.Material)
            .Include(i => i.Evidence)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<NonConformance> TransitionNonConformanceAsync(int id, NcrReviewRequest request, int reviewerUserId)
    {
        var ncr = await _context.NonConformances.FirstOrDefaultAsync(n => n.Id == id)
            ?? throw new KeyNotFoundException($"Non-conformance {id} not found.");
        var allowed = ncr.Status switch
        {
            NonConformanceStatus.Open => request.Status is NonConformanceStatus.UnderReview or NonConformanceStatus.CorrectiveActionRequired,
            NonConformanceStatus.UnderReview => request.Status is NonConformanceStatus.CorrectiveActionRequired or NonConformanceStatus.AcceptedException,
            NonConformanceStatus.CorrectiveActionRequired => request.Status is NonConformanceStatus.UnderReview or NonConformanceStatus.Resolved or NonConformanceStatus.AcceptedException,
            NonConformanceStatus.Resolved => request.Status is NonConformanceStatus.Closed,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"NCR transition from {ncr.Status} to {request.Status} is not allowed.");
        if (request.Status is NonConformanceStatus.Resolved or NonConformanceStatus.Closed or NonConformanceStatus.AcceptedException
            && string.IsNullOrWhiteSpace(request.Resolution)) throw new InvalidOperationException("A resolution is required to resolve, close, or accept an exception.");
        ncr.Status = request.Status;
        ncr.ReviewNotes = request.ReviewNotes?.Trim() ?? ncr.ReviewNotes;
        ncr.ResponsibleUserId = request.ResponsibleUserId ?? ncr.ResponsibleUserId;
        ncr.ReviewedByUserId = reviewerUserId;
        ncr.ReviewedAt = DateTime.UtcNow;
        ncr.Resolution = request.Resolution?.Trim() ?? ncr.Resolution;
        if (request.Status == NonConformanceStatus.Resolved) ncr.ResolvedAt = DateTime.UtcNow;
        if (request.Status == NonConformanceStatus.Closed) ncr.ClosedAt = DateTime.UtcNow;
        ncr.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return ncr;
    }

    public async Task<NonConformance> UpdateNonConformanceStatusAsync(int id, NonConformanceStatus newStatus)
    {
        var ncr = await _context.NonConformances.FindAsync(id);
        if (ncr == null)
            throw new KeyNotFoundException($"Non-conformance {id} not found.");

        ncr.Status = newStatus;
        await _context.SaveChangesAsync();
        return ncr;
    }
}
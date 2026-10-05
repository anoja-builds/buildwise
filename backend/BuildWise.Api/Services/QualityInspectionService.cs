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
        return await CompleteInspectionAsync(new Inspection
        {
            DeliveryId = dto.DeliveryId,
            InspectorUserId = inspectorUserId,
            InspectionCriteria = dto.InspectionCriteria,
            ObservedResult = dto.ObservedResult,
            Notes = dto.Notes,
            QuantityCheck = dto.QuantityCheck,
            VisualConditionCheck = dto.VisualConditionCheck,
            MoistureCheck = dto.MoistureCheck,
            PackagingCheck = dto.PackagingCheck,
            DefectsCheck = dto.DefectsCheck,
            Evidence = (dto.Evidence ?? new()).Select(e => new InspectionEvidence
            {
                FileName = e.FileName?.Trim() ?? string.Empty, FileUrl = e.FileUrl?.Trim() ?? string.Empty, ContentType = e.ContentType,
                FileSizeBytes = e.FileSizeBytes, UploadedByUserId = inspectorUserId, UploadedAt = DateTime.UtcNow
            }).ToList(),
            Items = (dto.Items ?? new()).Select(i => new InspectionItem
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
        if (inspection.DeliveryId <= 0)
            throw new InvalidOperationException("Delivery must be selected.");
        if (inspection.Items == null || inspection.Items.Count == 0)
            throw new InvalidOperationException("An inspection must contain at least one material line.");
        if (inspection.Evidence?.Count > 10)
            throw new InvalidOperationException("An inspection can contain at most 10 evidence files.");
        foreach (var evidence in inspection.Evidence ?? new())
        {
            if (string.IsNullOrWhiteSpace(evidence.FileName) || string.IsNullOrWhiteSpace(evidence.FileUrl)
                || (!evidence.FileUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    && !Uri.TryCreate(evidence.FileUrl, UriKind.Absolute, out _)))
                throw new InvalidOperationException("Each evidence record requires a file name and an absolute URL.");
            if (evidence.FileSizeBytes is < 0 or > 10_000_000)
                throw new InvalidOperationException("Evidence files must be 10 MB or less.");
            if (evidence.FileUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = evidence.FileUrl.IndexOf(',');
                if (comma < 0 || !evidence.FileUrl[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Evidence data must be a valid base64 file.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(evidence.FileUrl[(comma + 1)..]); }
                catch (FormatException) { throw new InvalidOperationException("Evidence data must be a valid base64 file."); }
                if (bytes.LongLength > 10_000_000)
                    throw new InvalidOperationException("Evidence files must be 10 MB or less.");
                evidence.FileSizeBytes = bytes.LongLength;
            }
        }
        if (new[] { inspection.QuantityCheck, inspection.VisualConditionCheck, inspection.MoistureCheck,
                inspection.PackagingCheck, inspection.DefectsCheck }.Any(check => check is false)
            && string.IsNullOrWhiteSpace(inspection.Notes))
            throw new InvalidOperationException("Notes are required when any checklist item fails.");

        // Rule 0: the five-point checklist is mandatory on completion.
        //
        // A completed inspection is a quality record, and "did nobody check the
        // packaging?" must be answerable from the data rather than inferred from
        // a blank free-text field. Enforcing it here (not in the column type)
        // keeps legacy rows readable while making every NEW inspection carry all
        // five points. A check may legitimately be false (it failed); it may not
        // be absent.
        var missingChecks = new List<string>();
        if (inspection.QuantityCheck is null) missingChecks.Add("quantity");
        if (inspection.VisualConditionCheck is null) missingChecks.Add("visual condition");
        if (inspection.MoistureCheck is null) missingChecks.Add("moisture");
        if (inspection.PackagingCheck is null) missingChecks.Add("packaging");
        if (inspection.DefectsCheck is null) missingChecks.Add("defects");
        if (missingChecks.Count > 0)
            throw new InvalidOperationException(
                "The five-point quality checklist is required: missing " +
                string.Join(", ", missingChecks) + ".");

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

        // Rule 1b: every inspected material must actually have arrived on THIS
        // delivery. Without this an inspector could post an inspection line for a
        // material the delivery never contained and the record would look valid
        // while referring to goods that were never received.
        //
        // "Available" is the quantity received on the delivery. Damaged units are
        // still inspected - deciding whether damaged goods are accepted or
        // rejected is exactly what this inspection is for - so damaged stock is
        // not subtracted here.
        var deliveredQuantities = await _context.DeliveryItems
            .Where(item => item.DeliveryId == inspection.DeliveryId)
            .GroupBy(item => item.MaterialId)
            .Select(group => new
            {
                MaterialId = group.Key,
                Available = group.Sum(item => item.ReceivedQuantity)
            })
            .ToListAsync();

        var availableByMaterial = deliveredQuantities.ToDictionary(x => x.MaterialId, x => x.Available);

        foreach (var item in inspection.Items)
        {
            if (!availableByMaterial.TryGetValue(item.MaterialId, out var available))
                throw new InvalidOperationException(
                    $"Material {item.MaterialId} is not part of delivery #{inspection.DeliveryId}. " +
                    "Only materials recorded as received on this delivery can be inspected.");

            if (item.AcceptedQuantity < 0 || item.RejectedQuantity < 0 || item.InspectedQuantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Inspected quantity must be greater than zero; accepted and rejected quantities cannot be negative for material {item.MaterialId}.");
            }

            if (item.InspectedQuantity > available)
            {
                throw new InvalidOperationException(
                    $"Inspected quantity cannot exceed the available quantity for material {item.MaterialId}. " +
                    $"Available {available:0.##}, inspected {item.InspectedQuantity:0.##}.");
            }

            if (item.RejectedQuantity > item.InspectedQuantity)
            {
                throw new InvalidOperationException(
                    $"Rejected quantity cannot exceed inspected quantity for material {item.MaterialId}. " +
                    $"Inspected {item.InspectedQuantity:0.##}, rejected {item.RejectedQuantity:0.##}.");
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

        foreach (var group in inspection.Items.GroupBy(item => item.MaterialId))
            if (group.Sum(item => item.InspectedQuantity) > availableByMaterial[group.Key])
                throw new InvalidOperationException($"Total inspected quantity cannot exceed received quantity for material {group.Key}.");

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
        // The decision is a three-way classification, not a two-way one:
        //   Rejected          every line was refused outright (AcceptedQuantity == 0)
        //   PartiallyAccepted some material passed and some did not
        //   Accepted          nothing was rejected
        // Validation guarantees Accepted + Rejected == Inspected and that
        // Inspected > 0, so AcceptedQuantity == 0 means the whole line was rejected.
        // Previously any rejection mapped to PartiallyAccepted, which made
        // InspectionDecision.Rejected unreachable and recorded a consignment the
        // inspector refused outright as a partial acceptance.
        var allLinesRejected = inspection.Items.Count > 0
            && inspection.Items.All(i => i.AcceptedQuantity == 0);
        inspection.OverallDecision = allLinesRejected
            ? InspectionDecision.Rejected
            : hasRejections
                ? InspectionDecision.PartiallyAccepted
                : InspectionDecision.Accepted;
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

    /// <summary>
    /// The authoritative NCR lifecycle. Every status change must pass through
    /// this map — <see cref="TransitionNonConformanceAsync"/> and the
    /// <c>PUT .../status</c> path both use it, so the second endpoint cannot be
    /// used to skip a step the first one enforces. <c>Closed</c> and
    /// <c>AcceptedException</c> are terminal.
    /// </summary>
    internal static bool IsAllowedTransition(NonConformanceStatus from, NonConformanceStatus to)
    {
        if (from == to) return false;

        return (from, to) switch
        {
            (NonConformanceStatus.Open, NonConformanceStatus.UnderReview) => true,
            (NonConformanceStatus.Open, NonConformanceStatus.CorrectiveActionRequired) => true,

            (NonConformanceStatus.UnderReview, NonConformanceStatus.CorrectiveActionRequired) => true,
            (NonConformanceStatus.UnderReview, NonConformanceStatus.AcceptedException) => true,

            (NonConformanceStatus.CorrectiveActionRequired, NonConformanceStatus.UnderReview) => true,
            (NonConformanceStatus.CorrectiveActionRequired, NonConformanceStatus.Resolved) => true,
            (NonConformanceStatus.CorrectiveActionRequired, NonConformanceStatus.AcceptedException) => true,

            (NonConformanceStatus.Resolved, NonConformanceStatus.Closed) => true,

            // Closed and AcceptedException are terminal.
            _ => false
        };
    }

    /// <summary>
    /// Statuses that represent a finished defect, and therefore need a written
    /// resolution before they can be entered.
    /// </summary>
    internal static bool RequiresResolution(NonConformanceStatus status) =>
        status is NonConformanceStatus.Resolved
            or NonConformanceStatus.Closed
            or NonConformanceStatus.AcceptedException;

    public async Task<NonConformance> TransitionNonConformanceAsync(int id, NcrReviewRequest request, int reviewerUserId)
    {
        var ncr = await _context.NonConformances.FirstOrDefaultAsync(n => n.Id == id)
            ?? throw new KeyNotFoundException($"Non-conformance {id} not found.");

        if (!IsAllowedTransition(ncr.Status, request.Status))
            throw new InvalidOperationException($"NCR transition from {ncr.Status} to {request.Status} is not allowed.");

        if (RequiresResolution(request.Status) && string.IsNullOrWhiteSpace(request.Resolution))
            throw new InvalidOperationException("A resolution is required to resolve, close, or accept an exception.");

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

    /// <summary>
    /// Status change for the simpler <c>PUT .../status</c> endpoint.
    /// <para>
    /// This used to assign <c>ncr.Status</c> directly, which meant a caller could
    /// jump an NCR straight to <c>Closed</c> with no resolution, no review
    /// metadata and no audit trail — bypassing every rule the transition endpoint
    /// enforces. It now delegates to the same transition map and stamps the same
    /// audit fields. The endpoint is kept because callers use it, but it is no
    /// longer a way around the lifecycle.
    /// </para>
    /// </summary>
    public async Task<NonConformance> UpdateNonConformanceStatusAsync(
        int id, NonConformanceStatus newStatus, string? resolution = null, int? reviewerUserId = null)
    {
        var ncr = await _context.NonConformances.FindAsync(id);
        if (ncr == null)
            throw new KeyNotFoundException($"Non-conformance {id} not found.");

        if (!IsAllowedTransition(ncr.Status, newStatus))
            throw new InvalidOperationException($"NCR transition from {ncr.Status} to {newStatus} is not allowed.");

        if (RequiresResolution(newStatus) && string.IsNullOrWhiteSpace(resolution))
            throw new InvalidOperationException("A resolution is required to resolve, close, or accept an exception.");

        ncr.Status = newStatus;
        if (!string.IsNullOrWhiteSpace(resolution))
            ncr.Resolution = resolution.Trim();

        if (reviewerUserId.HasValue)
        {
            ncr.ReviewedByUserId = reviewerUserId.Value;
            ncr.ReviewedAt = DateTime.UtcNow;
        }

        if (newStatus == NonConformanceStatus.Resolved) ncr.ResolvedAt = DateTime.UtcNow;
        if (newStatus == NonConformanceStatus.Closed) ncr.ClosedAt = DateTime.UtcNow;
        ncr.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ncr;
    }
}
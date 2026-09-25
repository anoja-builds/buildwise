using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class MaterialRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly OperationalAgentClient? _agentClient;
    private readonly OperationalAgentAuditService? _auditService;

    public MaterialRequestService(
        ApplicationDbContext context,
        OperationalAgentClient? agentClient = null,
        OperationalAgentAuditService? auditService = null)
    {
        _context = context;
        _agentClient = agentClient;
        _auditService = auditService;
    }

    public async Task<MaterialRequest> CreateRequestAsync(MaterialRequest request, int? authenticatedUserId = null)
    {
        if (authenticatedUserId.HasValue) request.RequestedByUserId = authenticatedUserId.Value;
        if (request.RequestDate == default) request.RequestDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.Priority == default) request.Priority = MaterialRequestPriority.Normal;

        var project = await _context.Projects.FindAsync(request.ProjectId);
        if (project == null || project.Status != ProjectStatus.Active)
            throw new InvalidOperationException("Material requests can only be created for Active projects.");
        if (request.RequiredDate < request.RequestDate)
            throw new InvalidOperationException("Required date cannot be earlier than the request date.");
        if (request.RequiredDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3))
            throw new InvalidOperationException("Required date must be at least 3 days in the future.");
        if (request.Items == null || request.Items.Count == 0)
            throw new InvalidOperationException("Material request must contain at least one item.");
        if (request.Items.Any(item => item.RequestedQuantity <= 0))
            throw new InvalidOperationException("Requested quantity must be greater than zero for every item.");
        if (request.Items.Any(item => item.RequiredDate.HasValue && item.RequiredDate.Value < request.RequestDate))
            throw new InvalidOperationException("Item required date cannot be earlier than the request date.");

        var materialIds = request.Items.Select(item => item.MaterialId).Distinct().ToList();
        var activeCount = await _context.Materials.CountAsync(m => materialIds.Contains(m.Id) && m.IsActive);
        if (activeCount != materialIds.Count)
            throw new InvalidOperationException("Every material request item must reference an Active material.");

        request.Status = MaterialRequestStatus.PendingApproval;
        request.CreatedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        _context.MaterialRequests.Add(request);
        await _context.SaveChangesAsync();
        await AddHistoryAsync(request.Id, authenticatedUserId ?? request.RequestedByUserId, "Created", null, request.Status, "Material request submitted for approval.");
        return request;
    }

    public async Task<MaterialRequest> ReviseRequestAsync(int requestId, int userId, ReviseMaterialRequestRequestDto dto)
    {
        var original = await _context.MaterialRequests.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException($"Material request #{requestId} not found.");
        if (original.Status is MaterialRequestStatus.Cancelled or MaterialRequestStatus.Completed)
            throw new InvalidOperationException("Cancelled or completed material requests cannot be revised.");
        if (original.RequestedByUserId != userId)
            throw new InvalidOperationException("Only the requesting site user can revise this material request.");
        if (dto.Items == null || dto.Items.Count == 0 || dto.Items.Any(i => i.RequestedQuantity <= 0))
            throw new InvalidOperationException("A revision must contain at least one item with positive quantity.");
        if (dto.RequiredDate < original.RequestDate || dto.RequiredDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3))
            throw new InvalidOperationException("The revised required date is invalid.");
        if (!Enum.TryParse<MaterialRequestPriority>(dto.Priority, true, out var priority))
            throw new InvalidOperationException("Priority must be Low, Normal, High, or Urgent.");

        var materialIds = dto.Items.Select(i => i.MaterialId).Distinct().ToList();
        var activeCount = await _context.Materials.CountAsync(m => materialIds.Contains(m.Id) && m.IsActive);
        if (activeCount != materialIds.Count) throw new InvalidOperationException("Every revised item must reference an Active material.");

        var revision = new MaterialRequest
        {
            ProjectId = original.ProjectId, RequestedByUserId = userId,
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow), RequiredDate = dto.RequiredDate,
            Priority = priority, Reason = dto.Reason?.Trim(), SiteNotes = dto.SiteNotes?.Trim(),
            RevisionOfRequestId = original.Id, RevisionNumber = original.RevisionNumber + 1,
            Status = MaterialRequestStatus.PendingApproval,
            Items = dto.Items.Select(i => new MaterialRequestItem
            {
                MaterialId = i.MaterialId, RequestedQuantity = i.RequestedQuantity, Unit = i.Unit?.Trim(),
                Description = i.Description?.Trim(), RequiredDate = i.RequiredDate, Notes = i.Notes?.Trim()
            }).ToList()
        };
        _context.MaterialRequests.Add(revision);
        await _context.SaveChangesAsync();
        await AddHistoryAsync(revision.Id, userId, "RevisionCreated", null, revision.Status, $"Revision {revision.RevisionNumber} of request #{original.Id}.");
        await AddHistoryAsync(original.Id, userId, "RevisionCreated", original.Status, original.Status, $"Created revision #{revision.Id}.");
        return revision;
    }

    public async Task<IReadOnlyList<MaterialRequestHistory>> GetHistoryAsync(int requestId)
    {
        if (!await _context.MaterialRequests.AnyAsync(r => r.Id == requestId)) throw new KeyNotFoundException($"Material request #{requestId} not found.");
        return await _context.MaterialRequestHistories.Where(h => h.MaterialRequestId == requestId).OrderBy(h => h.CreatedAt).ToListAsync();
    }

    public async Task<MaterialRequest> TransitionAsync(int requestId, int userId, MaterialRequestStatus target, string? reason = null)
    {
        var request = await _context.MaterialRequests.FirstOrDefaultAsync(r => r.Id == requestId) ?? throw new KeyNotFoundException($"Material request #{requestId} not found.");
        var allowed = request.Status switch
        {
            MaterialRequestStatus.Draft or MaterialRequestStatus.Submitted => target is MaterialRequestStatus.PendingApproval or MaterialRequestStatus.Cancelled,
            MaterialRequestStatus.PendingApproval or MaterialRequestStatus.UnderReview => target is MaterialRequestStatus.UnderReview or MaterialRequestStatus.RfqInProgress or MaterialRequestStatus.AwaitingProcurementApproval or MaterialRequestStatus.Cancelled,
            MaterialRequestStatus.RfqInProgress => target is MaterialRequestStatus.AwaitingProcurementApproval or MaterialRequestStatus.Cancelled,
            MaterialRequestStatus.AwaitingProcurementApproval => target is MaterialRequestStatus.Approved or MaterialRequestStatus.Cancelled,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Transition from {request.Status} to {target} is not allowed.");
        if (target == MaterialRequestStatus.Cancelled && string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Cancellation requires a reason.");
        var from = request.Status; request.Status = target; request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(); await AddHistoryAsync(requestId, userId, "StatusChanged", from, target, reason);
        return request;
    }

    private async Task AddHistoryAsync(int requestId, int? userId, string action, MaterialRequestStatus? from, MaterialRequestStatus? to, string? details)
    {
        _context.MaterialRequestHistories.Add(new MaterialRequestHistory { MaterialRequestId = requestId, ChangedByUserId = userId, Action = action, FromStatus = from, ToStatus = to, Details = details, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task<RequestAgentResult> AnalyzeRequestAsync(int requestId, int? initiatedByUserId = null)
    {
        var request = await _context.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException($"Material request #{requestId} not found.");

        var result = _agentClient == null
            ? new AgentClientResult<RequestAgentResult>(
                RequestAgentResult.Fallback(
                    request.Id,
                    request.Items.Count,
                    request.Items.Sum(item => item.RequestedQuantity),
                    request.Reason ?? string.Empty),
                "DeterministicFallback")
            : await _agentClient.AnalyzeRequestAsync(
                request.Id,
                request.Project?.Name ?? $"Project #{request.ProjectId}",
                request.Reason ?? string.Empty,
                request.Items.Count,
                request.Items.Sum(item => item.RequestedQuantity));

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(
                initiatedByUserId ?? request.RequestedByUserId,
                $"Identify planning risks for material request #{request.Id}.",
                "RequestAnalysisAgent",
                new[] { "Read validated request facts", "Call analyze_request", "Return non-authoritative flags" },
                new[] { "analyze_request" },
                result.Output,
                result.ExecutionSource,
                new { valid = result.Output.RequestId == request.Id && result.Output.Status == "Analyzed", businessRule = "advisory-only" },
                materialRequestId: request.Id);
        }

        return result.Output;
    }

    public async Task<Approval> RecordApprovalAsync(int requestId, int userId, ApprovalDecision decision, string? comments)
    {
        var mr = await _context.MaterialRequests.FindAsync(requestId);
        if (mr == null) throw new KeyNotFoundException("Material request not found.");

        if (mr.Status is MaterialRequestStatus.Cancelled or MaterialRequestStatus.Completed or MaterialRequestStatus.Approved or MaterialRequestStatus.Rejected)
            throw new InvalidOperationException("A terminal material request cannot be changed directly; create a linked revision instead.");
        if (decision == ApprovalDecision.Rejected && string.IsNullOrWhiteSpace(comments))
            throw new InvalidOperationException("A rejection reason is required.");
        if (decision == ApprovalDecision.Approved && mr.Status is not (MaterialRequestStatus.PendingApproval or MaterialRequestStatus.UnderReview or MaterialRequestStatus.AwaitingProcurementApproval))
            throw new InvalidOperationException("Only a submitted request can be approved.");

        var approval = new Approval
        {
            MaterialRequestId = requestId,
            ApprovedByUserId = userId,
            Decision = decision,
            Comments = comments,
            DecidedAt = DateTime.UtcNow
        };

        mr.Status = decision switch
        {
            ApprovalDecision.Approved => MaterialRequestStatus.Approved,
            ApprovalDecision.Rejected => MaterialRequestStatus.Rejected,
            _ => MaterialRequestStatus.PendingApproval
        };
        mr.UpdatedAt = DateTime.UtcNow;

        _context.Approvals.Add(approval);
        await _context.SaveChangesAsync();
        await AddHistoryAsync(requestId, userId, "ApprovalRecorded", null, mr.Status, comments);
        return approval;
    }
}

using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BuildWise.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

/// <summary>
/// Component 1: Material Request & Approval Management
/// Primary endpoints for Site Engineer/Officer to create requests and Procurement/Manager to approve.
/// </summary>
[ApiController]
[Route("api/material-requests")]
[Authorize(Policy = Policies.MaterialRequestReaders)]
public class MaterialRequestsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly MaterialRequestService _service;

    public MaterialRequestsController(ApplicationDbContext db, MaterialRequestService service)
    {
        _db = db;
        _service = service;
    }

    /// <summary>
    /// List material requests, optionally filtered by an explicit
    /// <paramref name="status"/>. Omitting the status (or passing "all") returns
    /// every status, so a request the site team just submitted is visible to the
    /// procurement desk while it is still PendingApproval — not only once it has
    /// been approved. An explicit value narrows the queue (Component 2 acts on
    /// Approved requests per spec §3, which callers can request directly).
    /// <para>
    /// Phase 1 RBAC fix: Site roles are scoped to the requests they raised
    /// themselves. Previously any Site Engineer or Site Officer could page
    /// through every other site user's requests across all projects.
    /// Procurement desk and approvers retain the full list.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MaterialRequestSummaryDto>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] int? projectId = null)
    {
        var query = _db.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
            .Include(r => r.Quotations)
            .AsQueryable();

        query = ApplySiteScope(query);

        // No status (or ?status= / ?status=all) means "every status" so an
        // approver's decision (PendingApproval -> Approved) stays visible in
        // the list. Explicit values still filter; the old default of Approved
        // hid every pending request from managers.
        if (!string.IsNullOrWhiteSpace(status)
            && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<MaterialRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        if (projectId.HasValue)
        {
            query = query.Where(r => r.ProjectId == projectId.Value);
        }

        var requests = await query
            .OrderByDescending(r => r.RequiredDate)
            .Select(r => new MaterialRequestSummaryDto(
                r.Id,
                r.ProjectId,
                r.Project!.Name,
                r.RequiredDate,
                r.Reason,
                r.Status.ToString(),
                r.Items.Count,
                r.Quotations.Count,
                r.RequestDate,
                r.Priority.ToString(),
                r.SiteNotes,
                r.RevisionOfRequestId,
                r.RevisionNumber
            ))
            .ToListAsync();

        return Ok(requests);
    }

    /// <summary>
    /// Returns the site team's material requests.
    /// <para>
    /// Read scope: site roles (Site Engineer / Site Officer) see the whole site
    /// queue, because they work the same site team and a request raised by a
    /// colleague is theirs to track — MR-58 must be visible to the Site Officer
    /// even though the Site Engineer raised it, and it was previously invisible
    /// to them purely because the filter was per-user. Procurement and approver
    /// roles are unaffected.
    /// </para>
    /// <para>
    /// Write scope is unchanged and deliberately narrower: a site user can only
    /// revise or submit a request they raised themselves, enforced by
    /// CanModifyRequest. Read visibility never implies write access.
    /// </para>
    /// </summary>
    [HttpGet("my")]
    [Authorize(Policy = Policies.SiteOperationsOnly)]
    public async Task<ActionResult<IEnumerable<MaterialRequestSummaryDto>>> GetMine()
    {
        var requests = await _db.MaterialRequests
            .Include(request => request.Project)
            .Include(request => request.Items)
            .Include(request => request.Quotations)
            .OrderByDescending(request => request.CreatedAt)
            .Select(request => new MaterialRequestSummaryDto(
                request.Id,
                request.ProjectId,
                request.Project!.Name,
                request.RequiredDate,
                request.Reason,
                request.Status.ToString(),
                request.Items.Count,
                request.Quotations.Count,
                request.RequestDate,
                request.Priority.ToString(),
                request.SiteNotes,
                request.RevisionOfRequestId,
                request.RevisionNumber))
            .ToListAsync();
        return Ok(requests);
    }

    /// <summary>
    /// Get a single material request with its line items, for quoting against.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaterialRequestDetailDto>> GetById(int id)
    {
        if (!await CanAccessRequest(id))
            return NotFound($"Material request #{id} not found.");

        var request = await _db.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
            .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null)
            return NotFound($"Material request #{id} not found.");

        var detail = new MaterialRequestDetailDto(
            request.Id,
            request.ProjectId,
            request.Project!.Name,
            request.RequiredDate,
            request.Reason,
            request.Status.ToString(),
            request.Items.Select(i => new MaterialRequestItemSummaryDto(
                i.Id, i.MaterialId, i.Material?.Name ?? $"Material #{i.MaterialId}",
                i.Unit ?? i.Material?.Unit ?? "Units", i.RequestedQuantity, i.Notes, i.Description, i.RequiredDate
            )).ToList(),
            request.RequestDate, request.Priority.ToString(), request.SiteNotes, request.RevisionOfRequestId, request.RevisionNumber
        );

        return Ok(detail);
    }

    /// <summary>
    /// Read-only, role-scoped procurement status for the Site Engineer's Flutter view (spec §9).
    /// Deliberately excludes supplier names, prices, and quotation detail — only the
    /// three states the mobile app is allowed to show: quotations in progress,
    /// awaiting manager approval, or purchase order created.
    /// </summary>
    [HttpGet("{id:int}/procurement-status")]
    public async Task<ActionResult<ProcurementStatusDto>> GetProcurementStatus(int id)
    {
        if (!await CanAccessRequest(id))
            return NotFound($"Material request #{id} not found.");

        var requestExists = await _db.MaterialRequests.AnyAsync(r => r.Id == id);
        if (!requestExists)
            return NotFound($"Material request #{id} not found.");

        var purchaseOrder = await _db.PurchaseOrders
            .Where(po => po.Quotation.MaterialRequestId == id && po.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(po => po.CreatedAt)
            .FirstOrDefaultAsync();

        if (purchaseOrder is not null)
        {
            return Ok(new ProcurementStatusDto(id, "PurchaseOrderCreated", purchaseOrder.Id, purchaseOrder.Status.ToString()));
        }

        var latestWorkflow = await _db.AgentWorkflows
            .Where(w => w.MaterialRequestId == id
                && w.Steps.Any(step => step.AgentRole == "QuotationSupplierAnalysisAgent"))
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestWorkflow is null)
        {
            return Ok(new ProcurementStatusDto(id, "NotStarted", null, null));
        }

        var status = latestWorkflow.Status switch
        {
            WorkflowStatus.AwaitingApproval => "AwaitingApproval",
            WorkflowStatus.Failed => "QuotationsInProgress",
            WorkflowStatus.Completed when latestWorkflow.ApprovalStatus == AgentApprovalStatus.Rejected => "Rejected",
            WorkflowStatus.Completed => "AwaitingApproval",
            _ => "QuotationsInProgress"
        };

        return Ok(new ProcurementStatusDto(id, status, null, null));
    }

    /// <summary>
    /// Component 1: Create a new material request (Site Engineer/Officer only)
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "SiteOperationsOnly")]
    public async Task<IActionResult> Create([FromBody] MaterialRequest request)
    {
        try
        {
            var userId = ParseUserId();
            var created = await _service.CreateRequestAsync(request, userId);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<IEnumerable<MaterialRequestHistoryDto>>> History(int id)
    {
        try
        {
            // Phase 1 RBAC, follow-up fix: this endpoint reads straight from
            // MaterialRequestService without the site-scope check that GetById
            // applies, so any Site Engineer / Site Officer could read the full
            // decision history — statuses, reviewer comments and who decided —
            // of every other site user's requests across all projects, while
            // the request itself returned 404. Answer 404 for the same reason
            // as GetById so the row's existence is not confirmed.
            if (!await CanAccessRequest(id)) return NotFound(new { message = $"Material request #{id} not found." });

            var rows = await _service.GetHistoryAsync(id);
            return Ok(rows.Select(h => new MaterialRequestHistoryDto(h.Id, h.Action, h.FromStatus?.ToString(), h.ToStatus?.ToString(), h.ChangedByUserId, h.Details, h.CreatedAt)));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = "SiteOperationsOnly")]
    public async Task<IActionResult> Submit(int id)
    {
        // Write scope: a site user may only submit their own request.
        if (!await CanModifyRequest(id)) return NotFound(new { message = $"Material request #{id} not found." });
        try { return Ok(await _service.TransitionAsync(id, ParseUserId(), MaterialRequestStatus.PendingApproval)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:int}/status")]
    [Authorize(Policy = Policies.ProcurementStaffAndAdmin)]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeMaterialRequestStatusDto dto)
    {
        if (!Enum.TryParse<MaterialRequestStatus>(dto.Status, true, out var status)) return BadRequest(new { message = "Invalid status." });
        try { return Ok(await _service.TransitionAsync(id, ParseUserId(), status, dto.Reason)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:int}/revise")]
    [Authorize(Policy = "SiteOperationsOnly")]
    public async Task<IActionResult> Revise(int id, [FromBody] ReviseMaterialRequestRequestDto dto)
    {
        try
        {
            // Write scope: a site user may only revise their own request.
            if (!await CanModifyRequest(id)) return NotFound(new { message = $"Material request #{id} not found." });
            var revision = await _service.ReviseRequestAsync(id, ParseUserId(), dto);
            return CreatedAtAction(nameof(GetById), new { id = revision.Id }, revision);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("~/api/agent/analyze-request/{requestId:int}")]
    [Authorize(Policy = Policies.MaterialRequestApprovalOnly)]
    public async Task<ActionResult<RequestAgentResult>> AnalyzeRequest(int requestId)
    {
        try
        {
            return Ok(await _service.AnalyzeRequestAsync(requestId, ParseUserId()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Component 1: Approve or reject a material request (Procurement/Manager only)
    /// </summary>
    [HttpPost("{id:int}/approval")]
    [Authorize(Policy = "MaterialRequestApprovalOnly")]
    public async Task<IActionResult> Approve(int id, [FromBody] ApprovalDecisionDto dto)
    {
        try
        {
            var userId = ParseUserId();
            var approval = await _service.RecordApprovalAsync(id, userId, dto.Decision, dto.Comments);
            return Ok(approval);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
    // --- Phase 1 RBAC: site data scoping -----------------------------------

    /// <summary>
    /// True when the caller is a site role (Site Engineer / Site Officer).
    /// Only these roles are narrowed to their own rows; procurement desk,
    /// approvers and administrators see the full portfolio.
    /// </summary>
    private bool IsSiteRole() => User.IsInAnyRole(Roles.SiteEngineer, Roles.SiteOfficer);

    /// <summary>
    /// Read scoping for list queries. Site roles see the whole site queue
    /// (see <see cref="CanAccessRequest"/>), so this is now a no-op and is kept
    /// only as the single place to reintroduce narrowing should project/team
    /// membership be modelled. Write narrowing lives in
    /// <see cref="CanModifyRequest"/>.
    /// </summary>
    private IQueryable<MaterialRequest> ApplySiteScope(IQueryable<MaterialRequest> query) => query;

    /// <summary>
    /// Read authorisation for a single material request.
    /// <para>
    /// Site roles may read any material request: they share one site queue, and
    /// a colleague's request has to stay visible so its status can be tracked.
    /// Previously this was narrowed to rows the caller raised, which hid
    /// MR-58 from the Site Officer entirely.
    /// </para>
    /// <para>
    /// Write authorisation stays narrow — see <see cref="CanModifyRequest"/>.
    /// </para>
    /// </summary>
    private Task<bool> CanAccessRequest(int id) => Task.FromResult(true);

    /// <summary>
    /// Write authorisation for a single material request. A site role may only
    /// change a request they raised themselves; read visibility never implies
    /// write access. Procurement desk, approvers and administrators are
    /// unaffected. Callers answer 404 (not 403) so the row's existence is not
    /// confirmed to a user with no claim to it.
    /// </summary>
    private async Task<bool> CanModifyRequest(int id)
    {
        if (!IsSiteRole()) return true;
        var userId = ParseUserId();
        return await _db.MaterialRequests
            .AsNoTracking()
            .AnyAsync(r => r.Id == id && r.RequestedByUserId == userId);
    }

    private int ParseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var userId) || userId <= 0)
            throw new InvalidOperationException("Authenticated user identifier is missing or invalid.");
        return userId;
    }
}

public record ApprovalDecisionDto(ApprovalDecision Decision, string? Comments);

public record ChangeMaterialRequestStatusDto(string Status, string? Reason);

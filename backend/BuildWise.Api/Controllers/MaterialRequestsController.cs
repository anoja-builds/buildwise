using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

/// <summary>
/// Read-only access to material requests for Component 2 (owned by Component 1).
/// Supports the "Approved Requests Queue" and Quotation Entry screens while
/// Component 1's own API surface is being developed in parallel.
/// </summary>
[ApiController]
[Route("api/material-requests")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,SiteEngineer,ProjectManager")]
public class MaterialRequestsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MaterialRequestsController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List material requests, optionally filtered by status (defaults to Approved,
    /// the only status Component 2 can act on per spec §3).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MaterialRequestSummaryDto>>> GetAll(
        [FromQuery] string? status = "Approved",
        [FromQuery] int? projectId = null)
    {
        var query = _db.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
            .Include(r => r.Quotations)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MaterialRequestStatus>(status, true, out var parsedStatus))
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
                r.Project.Name,
                r.RequiredDate,
                r.Reason,
                r.Status.ToString(),
                r.Items.Count,
                r.Quotations.Count
            ))
            .ToListAsync();

        return Ok(requests);
    }

    /// <summary>
    /// Get a single material request with its line items, for quoting against.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaterialRequestDetailDto>> GetById(int id)
    {
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
            request.Project.Name,
            request.RequiredDate,
            request.Reason,
            request.Status.ToString(),
            request.Items.Select(i => new MaterialRequestItemSummaryDto(
                i.Id,
                i.MaterialId,
                i.Material?.Name ?? $"Material #{i.MaterialId}",
                i.Material?.Unit ?? "Units",
                i.RequestedQuantity,
                i.Notes
            )).ToList()
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
            .Where(w => w.MaterialRequestId == id)
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
}

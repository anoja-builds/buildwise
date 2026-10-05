using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Security;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Policy = Policies.InternalStaffOnly)]
public class PurchaseOrdersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ProcurementWorkflowService _workflowService;
    private readonly ILogger<PurchaseOrdersController> _logger;

    public PurchaseOrdersController(
        ApplicationDbContext db,
        ProcurementWorkflowService workflowService,
        ILogger<PurchaseOrdersController> logger)
    {
        _db = db;
        _workflowService = workflowService;
        _logger = logger;
    }

    /// <summary>
    /// Explicitly trigger Purchase Order creation from an approved agent workflow (§4.6 / §7).
    /// </summary>
    [HttpPost("procurement-workflow/{workflowId:int}/purchase-order")]
    [Authorize(Policy = Policies.ProcurementDecisionOnly)]
    public async Task<ActionResult<object>> CreateFromWorkflow(int workflowId)
    {
        try
        {
            var po = await _workflowService.CreatePurchaseOrderFromWorkflowAsync(workflowId);
            return await GetById(po.Id);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// List purchase orders with search, status filtering, and pagination.
    /// Exposes read-only purchase orders to Component 3 once status >= Confirmed.
    /// <para>
    /// Commercial terms (<c>totalAmount</c>, per-line <c>unitPrice</c>) are
    /// included only for procurement-desk callers; site, receiving and quality
    /// roles receive the redacted <see cref="PurchaseOrderReceivingDto"/> shape.
    /// </para>
    /// </summary>
    [HttpGet("purchase-orders")]
    [Authorize(Policy = Policies.InternalStaffOnly)]
    public async Task<ActionResult<PagedResultDto<object>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.PurchaseOrders
            // C3 POs link the supplier directly; C2 POs via the winning quotation.
            // Both must be loaded or the projection falls back to "Supplier #n".
            .Include(po => po.Supplier)
            .Include(po => po.Quotation)
            .ThenInclude(q => q.Supplier)
            .Include(po => po.Items)
            .ThenInclude(poi => poi.Material)
            .Include(po => po.Items)
            .ThenInclude(poi => poi.QuotationItem)
            .ThenInclude(qi => qi.MaterialRequestItem)
            .ThenInclude(mri => mri.Material)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PurchaseOrderStatus>(status, true, out var poStatus))
        {
            query = query.Where(po => po.Status == poStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = search.Trim().ToLower();
            query = query.Where(po =>
                (po.Supplier != null && po.Supplier.Name.ToLower().Contains(cleanSearch)) ||
                (po.Quotation!.Supplier!.Name.ToLower().Contains(cleanSearch)) ||
                po.Id.ToString().Contains(cleanSearch));
        }

        var total = await query.CountAsync();

        var list = await query
            .OrderByDescending(po => po.OrderDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = list.ToCallerDto(User).ToList();
        return Ok(new PagedResultDto<object>(dtos, total, page, pageSize));
    }

    /// <summary>
    /// Get purchase order detail with items and linked quotation.
    /// Commercial terms are returned only to procurement-desk callers.
    /// </summary>
    [HttpGet("purchase-orders/{id:int}")]
    [Authorize(Policy = Policies.InternalStaffOnly)]
    public async Task<ActionResult<object>> GetById(int id)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Quotation)
            .ThenInclude(q => q.Supplier)
            .Include(p => p.Items)
            .ThenInclude(poi => poi.Material)
            .Include(p => p.Items)
            .ThenInclude(poi => poi.QuotationItem)
            .ThenInclude(qi => qi.MaterialRequestItem)
            .ThenInclude(mri => mri.Material)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null)
            return NotFound($"Purchase Order #{id} not found.");

        return Ok(po.ToCallerDto(User));
    }

    /// <summary>
    /// Update purchase order status (Confirmed, InProgress, Completed, Cancelled).
    /// Procurement desk only — site and quality roles are read-only on POs.
    /// </summary>
    [HttpPatch("purchase-orders/{id:int}/status")]
    [Authorize(Policy = Policies.ProcurementStaffOnly)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePurchaseOrderStatusDto dto)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po is null)
            return NotFound($"Purchase Order #{id} not found.");

        if (!Enum.TryParse<PurchaseOrderStatus>(dto.Status, true, out var newStatus))
            return BadRequest($"Invalid status '{dto.Status}'. Allowed: Confirmed, InProgress, Completed, Cancelled.");

        var oldStatus = po.Status;
        po.Status = newStatus;
        po.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Purchase Order #{Id} status changed from {OldStatus} to {NewStatus}", id, oldStatus, newStatus);

        return NoContent();
    }
}

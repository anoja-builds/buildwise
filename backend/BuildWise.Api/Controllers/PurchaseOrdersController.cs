using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator")]
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
    public async Task<ActionResult<PurchaseOrderDto>> CreateFromWorkflow(int workflowId)
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
    /// </summary>
    [HttpGet("purchase-orders")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,ReceivingOfficer")]
    public async Task<ActionResult<PagedResultDto<PurchaseOrderDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.PurchaseOrders
            .Include(po => po.Quotation)
            .ThenInclude(q => q.Supplier)
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
            query = query.Where(po => po.Quotation.Supplier.Name.ToLower().Contains(cleanSearch) ||
                                      po.Id.ToString().Contains(cleanSearch));
        }

        var total = await query.CountAsync();

        var list = await query
            .OrderByDescending(po => po.OrderDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = list.Select(MapToPurchaseOrderDto).ToList();
        return Ok(new PagedResultDto<PurchaseOrderDto>(dtos, total, page, pageSize));
    }

    /// <summary>
    /// Get purchase order detail with items and linked quotation.
    /// </summary>
    [HttpGet("purchase-orders/{id:int}")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,ReceivingOfficer")]
    public async Task<ActionResult<PurchaseOrderDto>> GetById(int id)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Quotation)
            .ThenInclude(q => q.Supplier)
            .Include(p => p.Items)
            .ThenInclude(poi => poi.QuotationItem)
            .ThenInclude(qi => qi.MaterialRequestItem)
            .ThenInclude(mri => mri.Material)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null)
            return NotFound($"Purchase Order #{id} not found.");

        return Ok(MapToPurchaseOrderDto(po));
    }

    /// <summary>
    /// Update purchase order status (Confirmed, InProgress, Completed, Cancelled).
    /// </summary>
    [HttpPatch("purchase-orders/{id:int}/status")]
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

    private static PurchaseOrderDto MapToPurchaseOrderDto(PurchaseOrder po)
    {
        // C2 POs carry the supplier via the winning quotation; C3 POs carry it
        // directly (SupplierId). Prefer the direct link, fall back to quotation.
        var supplierId = po.SupplierId ?? po.Quotation?.SupplierId ?? 0;
        var supplierName = po.Supplier?.Name
            ?? po.Quotation?.Supplier?.Name
            ?? $"Supplier #{supplierId}";
        return new PurchaseOrderDto(
            po.Id,
            po.QuotationId ?? 0,
            po.Quotation?.MaterialRequestId ?? 0,
            supplierId,
            supplierName,
            po.OrderDate,
            po.ExpectedDeliveryDate,
            po.Status.ToString(),
            po.TotalAmount,
            po.CreatedAt,
            po.UpdatedAt,
            po.Items.Select(i => new PurchaseOrderItemDto(
                i.Id,
                i.QuotationItemId,
                i.MaterialId,
                i.QuotationItem?.MaterialRequestItem?.Material?.Name
                    ?? i.Material?.Name
                    ?? $"Item #{i.QuotationItemId ?? i.MaterialId ?? i.Id}",
                i.QuotationItem?.MaterialRequestItem?.Material?.Unit ?? i.Material?.Unit ?? "Units",
                i.OrderedQuantity,
                i.UnitPrice,
                i.OrderedQuantity * i.UnitPrice
            )).ToList()
        );
    }
}

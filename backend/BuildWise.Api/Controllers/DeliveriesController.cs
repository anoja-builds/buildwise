using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using BuildWise.Api.Models.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DeliveriesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DeliveryRiskAgentService _riskAgentService;

    public DeliveriesController(
        ApplicationDbContext dbContext,
        DeliveryRiskAgentService riskAgentService)
    {
        _dbContext = dbContext;
        _riskAgentService = riskAgentService;
    }

    [HttpGet("expected")]
    [Authorize(Roles = "ReceivingOfficer,Administrator,ProcurementOfficer,ProcurementManager,QualityInspector,ProjectManager")]
    public async Task<IActionResult> GetExpectedDeliveries()
    {
        var deliveries = await _dbContext.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Project)
            .Include(d => d.Items)
                .ThenInclude(di => di.PurchaseOrderItem)
                    .ThenInclude(poi => poi!.Material)
            .Where(d => d.Status == DeliveryStatus.Scheduled || d.Status == DeliveryStatus.InTransit)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        var result = deliveries.Select(d => new
        {
            d.Id,
            d.PurchaseOrderId,
            SupplierName = d.PurchaseOrder?.Supplier?.Name,
            ProjectName = d.PurchaseOrder?.Project?.Name,
            d.DeliveryReference,
            d.Status,
            ExpectedDate = d.PurchaseOrder?.ExpectedDeliveryDate,
            ItemsCount = d.Items.Count,
            Items = d.Items.Select(di => new
            {
                di.Id,
                di.PurchaseOrderItemId,
                MaterialName = di.PurchaseOrderItem?.Material?.Name,
                MaterialUnit = di.PurchaseOrderItem?.Material?.Unit,
                OrderedQuantity = di.PurchaseOrderItem?.OrderedQuantity ?? 0,
                di.ReceivedQuantity,
                di.DamagedQuantity,
                di.Notes
            })
        });

        return Ok(result);
    }

    [HttpGet("history")]
    [Authorize(Roles = "ReceivingOfficer,Administrator,ProcurementOfficer,ProcurementManager,QualityInspector,ProjectManager")]
    public async Task<IActionResult> GetDeliveryHistory()
    {
        var deliveries = await _dbContext.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Project)
            .Include(d => d.ReceivedByUser)
            .Include(d => d.Items)
                .ThenInclude(di => di.PurchaseOrderItem)
                    .ThenInclude(poi => poi!.Material)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();

        var result = deliveries.Select(d => new
        {
            d.Id,
            d.PurchaseOrderId,
            SupplierName = d.PurchaseOrder?.Supplier?.Name,
            ProjectName = d.PurchaseOrder?.Project?.Name,
            d.DeliveryReference,
            d.Status,
            d.ActualArrivalDate,
            d.ReceivedAt,
            ReceivedBy = d.ReceivedByUser?.FullName,
            d.Notes,
            d.PhotographicEvidenceUrl,
            Items = d.Items.Select(di => new
            {
                di.Id,
                di.PurchaseOrderItemId,
                MaterialName = di.PurchaseOrderItem?.Material?.Name,
                MaterialUnit = di.PurchaseOrderItem?.Material?.Unit,
                OrderedQuantity = di.PurchaseOrderItem?.OrderedQuantity ?? 0,
                di.ReceivedQuantity,
                di.DamagedQuantity,
                ShortageQuantity = (di.PurchaseOrderItem?.OrderedQuantity ?? 0) - di.ReceivedQuantity,
                InspectionQuantity = di.ReceivedQuantity - di.DamagedQuantity,
                di.Notes
            })
        });

        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "ReceivingOfficer,Administrator,ProcurementOfficer,ProcurementManager,QualityInspector,ProjectManager")]
    public async Task<IActionResult> GetDeliveryById(int id)
    {
        var delivery = await _dbContext.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Project)
            .Include(d => d.ReceivedByUser)
            .Include(d => d.Items)
                .ThenInclude(di => di.PurchaseOrderItem)
                    .ThenInclude(poi => poi!.Material)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (delivery == null)
        {
            return NotFound("Delivery record not found.");
        }

        var result = new
        {
            delivery.Id,
            delivery.PurchaseOrderId,
            SupplierId = delivery.PurchaseOrder?.SupplierId,
            SupplierName = delivery.PurchaseOrder?.Supplier?.Name,
            ProjectName = delivery.PurchaseOrder?.Project?.Name,
            delivery.DeliveryReference,
            delivery.Status,
            delivery.ActualArrivalDate,
            delivery.ReceivedAt,
            delivery.Notes,
            delivery.PhotographicEvidenceUrl,
            ExpectedDate = delivery.PurchaseOrder?.ExpectedDeliveryDate,
            ReceivedBy = delivery.ReceivedByUser?.FullName,
            Items = delivery.Items.Select(di => new
            {
                di.Id,
                di.PurchaseOrderItemId,
                MaterialId = di.PurchaseOrderItem?.MaterialId,
                MaterialName = di.PurchaseOrderItem?.Material?.Name,
                MaterialUnit = di.PurchaseOrderItem?.Material?.Unit,
                OrderedQuantity = di.PurchaseOrderItem?.OrderedQuantity ?? 0,
                di.ReceivedQuantity,
                di.DamagedQuantity,
                ShortageQuantity = (di.PurchaseOrderItem?.OrderedQuantity ?? 0) - di.ReceivedQuantity,
                InspectionQuantity = di.ReceivedQuantity - di.DamagedQuantity,
                di.Notes
            })
        };

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,ReceivingOfficer")]
    public async Task<IActionResult> ScheduleDelivery([FromBody] CreateDeliveryDto dto)
    {
        var purchaseOrder = await _dbContext.PurchaseOrders
            .Include(po => po.Items)
            .FirstOrDefaultAsync(po => po.Id == dto.PurchaseOrderId);

        if (purchaseOrder == null)
        {
            return BadRequest("Purchase Order not found.");
        }

        var delivery = new Delivery
        {
            PurchaseOrderId = dto.PurchaseOrderId,
            DeliveryReference = dto.DeliveryReference,
            Status = DeliveryStatus.Scheduled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Deliveries.Add(delivery);
        await _dbContext.SaveChangesAsync();

        foreach (var item in purchaseOrder.Items)
        {
            var deliveryItem = new DeliveryItem
            {
                DeliveryId = delivery.Id,
                PurchaseOrderItemId = item.Id,
                ReceivedQuantity = 0,
                DamagedQuantity = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.DeliveryItems.Add(deliveryItem);
        }

        purchaseOrder.Status = PurchaseOrderStatus.InProgress;
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDeliveryById), new { id = delivery.Id }, delivery);
    }

    [HttpPost("{id}/receive")]
    [Authorize(Roles = "ReceivingOfficer,Administrator")]
    public async Task<IActionResult> ReceiveDelivery(int id, [FromBody] ReceiveDeliveryDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        // Serialize concurrent receipts on relational databases so two deliveries
        // cannot both reconcile against the same stale order quantities.
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        var delivery = await _dbContext.Deliveries
            .Include(d => d.Items).ThenInclude(i => i.PurchaseOrderItem)
            .Include(d => d.PurchaseOrder).ThenInclude(p => p!.Items)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (delivery == null) return NotFound("Delivery not found.");
        if (delivery.ReceivedAt != null || delivery.Status is DeliveryStatus.Received
            or DeliveryStatus.DiscrepancyReported or DeliveryStatus.PartiallyReceived)
            return BadRequest("This delivery has already been processed.");
        if (delivery.PurchaseOrder == null || delivery.PurchaseOrder.Items.Count == 0)
            return BadRequest("Delivery must belong to a purchase order with items.");
        if (delivery.PurchaseOrder.Status == PurchaseOrderStatus.Cancelled)
            return BadRequest("Cannot receive a cancelled purchase order.");
        if (!await _dbContext.Users.AnyAsync(u => u.Id == actorId && u.IsActive))
            return Forbid();

        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest("At least one delivery item is required.");
        if (dto.Items.Select(i => i.PurchaseOrderItemId).Distinct().Count() != dto.Items.Count)
            return BadRequest("Duplicate purchase-order item IDs are not allowed.");
        var deliveryItems = delivery.Items.ToDictionary(i => i.PurchaseOrderItemId);
        if (dto.Items.Any(i => !deliveryItems.ContainsKey(i.PurchaseOrderItemId)
            || deliveryItems[i.PurchaseOrderItemId].PurchaseOrderItem?.PurchaseOrderId != delivery.PurchaseOrderId))
            return BadRequest("Every item must belong to this delivery and its purchase order.");
        if (dto.Items.Count != deliveryItems.Count)
            return BadRequest("Supply every delivery item, using zero for items not received.");
        if (dto.Items.Any(i => i.ReceivedQuantity < 0 || i.DamagedQuantity < 0 || i.DamagedQuantity > i.ReceivedQuantity))
            return BadRequest("Quantities cannot be negative and damaged quantity cannot exceed received quantity.");

        // Read only other finalized deliveries. The current receipt is added
        // exactly once below, independent of EF tracking/provider behavior.
        var priorItems = await _dbContext.DeliveryItems.AsNoTracking()
            .Where(i => i.DeliveryId != id && i.Delivery!.PurchaseOrderId == delivery.PurchaseOrderId
                && (i.Delivery.Status == DeliveryStatus.Received
                    || i.Delivery.Status == DeliveryStatus.PartiallyReceived
                    || i.Delivery.Status == DeliveryStatus.DiscrepancyReported))
            .ToListAsync();
        var priorUsable = priorItems.GroupBy(i => i.PurchaseOrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.ReceivedQuantity - i.DamagedQuantity));
        foreach (var item in dto.Items)
        {
            var remaining = Math.Max(0, deliveryItems[item.PurchaseOrderItemId].PurchaseOrderItem!.OrderedQuantity
                - priorUsable.GetValueOrDefault(item.PurchaseOrderItemId));
            if (item.ReceivedQuantity > remaining)
                return BadRequest($"Received quantity for item #{item.PurchaseOrderItemId} exceeds the outstanding quantity ({remaining}).");
        }

        var now = DateTime.UtcNow;
        foreach (var item in dto.Items)
        {
            var stored = deliveryItems[item.PurchaseOrderItemId];
            stored.ReceivedQuantity = item.ReceivedQuantity;
            stored.DamagedQuantity = item.DamagedQuantity;
            stored.Notes = item.Notes;
            stored.UpdatedAt = now;
        }
        var complete = delivery.PurchaseOrder.Items.All(i =>
            priorUsable.GetValueOrDefault(i.Id) + delivery.Items.Where(d => d.PurchaseOrderItemId == i.Id)
                .Sum(d => d.ReceivedQuantity - d.DamagedQuantity) >= i.OrderedQuantity);
        // Preserve the existing shortage/damage discrepancy workflow used by quality.
        delivery.Status = dto.Items.Any(i => i.DamagedQuantity > 0) || !complete
            ? DeliveryStatus.DiscrepancyReported : DeliveryStatus.Received;
        delivery.ReceivedByUserId = actorId;
        delivery.Notes = dto.Notes;
        delivery.ActualArrivalDate = now;
        delivery.ReceivedAt = now;
        delivery.UpdatedAt = now;
        delivery.PurchaseOrder.Status = complete ? PurchaseOrderStatus.Completed : PurchaseOrderStatus.InProgress;
        delivery.PurchaseOrder.UpdatedAt = now;
        await _dbContext.SaveChangesAsync();
        if (transaction != null) await transaction.CommitAsync();

        return Ok(new
        {
            Message = "Delivery received successfully reconciled.",
            DeliveryId = delivery.Id,
            Status = delivery.Status.ToString()
        });
    }

    [HttpPost("{id}/evidence")]
    [Authorize(Roles = "ReceivingOfficer,Administrator")]
    public async Task<IActionResult> AddPhotographicEvidence(int id, [FromBody] EvidenceDto dto)
    {
        var delivery = await _dbContext.Deliveries.FindAsync(id);
        if (delivery == null)
        {
            return NotFound("Delivery not found.");
        }

        delivery.PhotographicEvidenceUrl = dto.ImageUrl;
        delivery.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = "Photographic evidence attached successfully.", ImageUrl = dto.ImageUrl });
    }

    [HttpGet("schedules")]
    [Authorize(Roles = "ReceivingOfficer,Administrator,ProcurementOfficer,ProcurementManager,QualityInspector,ProjectManager")]
    public async Task<IActionResult> GetSchedules()
    {
        var schedules = await _dbContext.DeliverySchedules
            .Include(s => s.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
            .Include(s => s.PurchaseOrder)
                .ThenInclude(po => po!.Project)
            .OrderBy(s => s.ScheduledDate)
            .ToListAsync();

        return Ok(schedules);
    }

    [HttpPost("schedule")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,ReceivingOfficer")]
    public async Task<IActionResult> CreateSchedule([FromBody] ScheduleDeliveryDto dto)
    {
        var schedule = new DeliverySchedule
        {
            PurchaseOrderId = dto.PurchaseOrderId,
            ScheduledDate = dto.ScheduledDate,
            ScheduledTimeSlot = dto.TimeSlot,
            Status = "Confirmed",
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.DeliverySchedules.Add(schedule);
        await _dbContext.SaveChangesAsync();

        return Ok(schedule);
    }

    [HttpGet("issues")]
    [Authorize(Roles = "ReceivingOfficer,Administrator,ProcurementOfficer,ProcurementManager,QualityInspector,ProjectManager")]
    public async Task<IActionResult> GetIssues()
    {
        var issues = await _dbContext.DeliveryIssues
            .Include(i => i.Delivery)
            .Include(i => i.ReportedByUser)
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync();

        return Ok(issues);
    }

    [HttpPost("report-issue")]
    [Authorize(Roles = "ReceivingOfficer,Administrator")]
    public async Task<IActionResult> ReportIssue([FromBody] ReportIssueDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        var issue = new DeliveryIssue
        {
            DeliveryId = dto.DeliveryId,
            DeliveryItemId = dto.DeliveryItemId,
            IssueType = dto.IssueType,
            Description = dto.Description,
            Severity = dto.Severity,
            ReportedByUserId = actorId,
            Status = "Open",
            ReportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.DeliveryIssues.Add(issue);
        await _dbContext.SaveChangesAsync();

        return Ok(issue);
    }

    [HttpPost("evaluate-risk/{purchaseOrderId}")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,ReceivingOfficer")]
    public async Task<IActionResult> EvaluateDeliveryRisk(int purchaseOrderId, [FromQuery] int? userId)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        try
        {
            var assessment = await _riskAgentService.EvaluateDeliveryRiskAsync(purchaseOrderId, actorId);
            return Ok(assessment);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred during evaluation: {ex.Message}");
        }
    }

    [HttpPost("{id}/risk-analysis")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,ReceivingOfficer")]
    public async Task<IActionResult> AnalyzeDeliveryRisk(int id, [FromQuery] int? userId)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        try
        {
            var delivery = await _dbContext.Deliveries.FindAsync(id);
            if (delivery == null) return NotFound("Delivery not found.");

            var assessment = await _riskAgentService.EvaluateDeliveryRiskAsync(delivery.PurchaseOrderId, actorId, id);
            return Ok(assessment);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}

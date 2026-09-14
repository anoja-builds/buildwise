using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using BuildWise.Api.Models.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

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
    public async Task<IActionResult> ReceiveDelivery(int id, [FromBody] ReceiveDeliveryDto dto)
    {
        var delivery = await _dbContext.Deliveries
            .Include(d => d.Items)
                .ThenInclude(di => di.PurchaseOrderItem)
            .Include(d => d.PurchaseOrder)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (delivery == null)
        {
            return NotFound("Delivery not found.");
        }

        if (delivery.Status == DeliveryStatus.Received || delivery.Status == DeliveryStatus.DiscrepancyReported)
        {
            return BadRequest("This delivery has already been processed.");
        }

        // Retrieve user
        var user = await _dbContext.Users.FindAsync(dto.ReceivedByUserId);
        if (user == null)
        {
            return BadRequest("Valid receiving officer/user is required.");
        }

        delivery.ReceivedByUserId = dto.ReceivedByUserId;
        delivery.Notes = dto.Notes;
        delivery.ActualArrivalDate = DateTime.UtcNow;
        delivery.ReceivedAt = DateTime.UtcNow;
        delivery.UpdatedAt = DateTime.UtcNow;

        bool hasDiscrepancy = false;
        bool allCompleted = true;

        foreach (var itemDto in dto.Items)
        {
            var deliveryItem = delivery.Items
                .FirstOrDefault(di => di.PurchaseOrderItemId == itemDto.PurchaseOrderItemId);

            if (deliveryItem == null) continue;

            deliveryItem.ReceivedQuantity = itemDto.ReceivedQuantity;
            deliveryItem.DamagedQuantity = itemDto.DamagedQuantity;
            deliveryItem.Notes = itemDto.Notes;
            deliveryItem.UpdatedAt = DateTime.UtcNow;

            var orderedQty = deliveryItem.PurchaseOrderItem?.OrderedQuantity ?? 0;
            var shortage = orderedQty - itemDto.ReceivedQuantity;

            if (shortage > 0 || itemDto.DamagedQuantity > 0)
            {
                hasDiscrepancy = true;
            }

            if (itemDto.ReceivedQuantity < orderedQty)
            {
                allCompleted = false;
            }
        }

        if (hasDiscrepancy)
        {
            delivery.Status = DeliveryStatus.DiscrepancyReported;
        }
        else if (allCompleted)
        {
            delivery.Status = DeliveryStatus.Received;
        }
        else
        {
            delivery.Status = DeliveryStatus.PartiallyReceived;
        }

        // Check if all items in the Purchase Order are fully received across all deliveries
        if (delivery.PurchaseOrder != null)
        {
            // If this delivery was received successfully or partially
            var allPoItems = await _dbContext.PurchaseOrderItems
                .Where(poi => poi.PurchaseOrderId == delivery.PurchaseOrderId)
                .ToListAsync();

            bool isPoComplete = true;
            foreach (var poItem in allPoItems)
            {
                var totalReceivedForPoItem = await _dbContext.DeliveryItems
                    .Where(di => di.PurchaseOrderItemId == poItem.Id && di.Delivery!.Status != DeliveryStatus.Scheduled && di.Delivery!.Status != DeliveryStatus.InTransit)
                    .SumAsync(di => di.ReceivedQuantity) + dto.Items.FirstOrDefault(i => i.PurchaseOrderItemId == poItem.Id)?.ReceivedQuantity ?? 0;

                if (totalReceivedForPoItem < poItem.OrderedQuantity)
                {
                    isPoComplete = false;
                    break;
                }
            }

            delivery.PurchaseOrder.Status = isPoComplete ? PurchaseOrderStatus.Completed : PurchaseOrderStatus.InProgress;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            Message = "Delivery received successfully reconciled.",
            DeliveryId = delivery.Id,
            Status = delivery.Status.ToString()
        });
    }

    [HttpPost("{id}/evidence")]
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
    public async Task<IActionResult> ReportIssue([FromBody] ReportIssueDto dto)
    {
        var issue = new DeliveryIssue
        {
            DeliveryId = dto.DeliveryId,
            DeliveryItemId = dto.DeliveryItemId,
            IssueType = dto.IssueType,
            Description = dto.Description,
            Severity = dto.Severity,
            ReportedByUserId = dto.ReportedByUserId,
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
    public async Task<IActionResult> EvaluateDeliveryRisk(int purchaseOrderId, [FromQuery] int? userId)
    {
        try
        {
            var assessment = await _riskAgentService.EvaluateDeliveryRiskAsync(purchaseOrderId, userId);
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
}

using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using BuildWise.Api.Models.Dtos;
using DeliveryStatusEnum = BuildWise.Api.Models.Enums.DeliveryStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeliveriesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DeliveryService _deliveryService;

    public DeliveriesController(
        ApplicationDbContext dbContext,
        DeliveryService deliveryService)
    {
        _dbContext = dbContext;
        _deliveryService = deliveryService;
    }

    [HttpGet("confirmed-orders")]
    public async Task<IActionResult> GetConfirmedOrders()
    {
        var orders = await _deliveryService.GetConfirmedPurchaseOrdersAsync();
        return Ok(orders);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var deliveries = await _deliveryService.GetDeliveriesAsync();
        return Ok(deliveries);
    }

    [HttpPost]
    [Authorize(Policy = "SiteOperationsOnly")]
    public async Task<IActionResult> Record([FromBody] Delivery delivery)
    {
        try
        {
            delivery.ReceivedByUserId = ParseUserId();
            var created = await _deliveryService.RecordDeliveryAsync(delivery);
            return CreatedAtAction(nameof(GetAll), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
                .ThenInclude(di => di.Material)
            .Where(d => d.Status == DeliveryStatusEnum.Scheduled || d.Status == DeliveryStatusEnum.InTransit)
            .OrderByDescending(d => d.DeliveredAt)
            .ToListAsync();

        var result = deliveries.Select(d => new
        {
            d.Id,
            d.PurchaseOrderId,
            SupplierName = d.PurchaseOrder?.Supplier?.Name,
            ProjectName = d.PurchaseOrder?.Project?.Name,
            d.DeliveryReference,
            d.Status,
            d.DeliveredAt,
            ItemsCount = d.Items.Count,
            Items = d.Items.Select(di => new
            {
                di.Id,
                di.MaterialId,
                MaterialName = di.Material?.Name,
                MaterialUnit = di.Material?.Unit,
                di.ReceivedQuantity,
                di.DamagedQuantity
            })
        });

        return Ok(result);
    }

    private int ParseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var userId) || userId <= 0)
            throw new InvalidOperationException("Authenticated user identifier is missing or invalid.");
        return userId;
    }
}
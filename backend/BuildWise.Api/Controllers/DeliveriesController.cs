using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Security;
using BuildWise.Api.Services;
using BuildWise.Api.Models.Dtos;
using DeliveryStatusEnum = BuildWise.Api.Models.Enums.DeliveryStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.DeliveryParticipantsOnly)]
public class DeliveriesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DeliveryService _deliveryService;
    private readonly OperationalAgentClient _agentClient;

    public DeliveriesController(
        ApplicationDbContext dbContext,
        DeliveryService deliveryService,
        OperationalAgentClient agentClient)
    {
        _dbContext = dbContext;
        _deliveryService = deliveryService;
        _agentClient = agentClient;
    }

    /// <summary>
    /// Confirmed purchase orders available for receiving.
    /// <para>
    /// Phase 1 RBAC fix: this previously returned raw <see cref="PurchaseOrder"/>
    /// entities, so every authenticated caller — including Site Officers and
    /// Quality Inspectors — received <c>TotalAmount</c> and every per-line
    /// <c>UnitPrice</c>. It now returns an explicit receiving DTO with no
    /// commercial terms, and requires a delivery-participant role.
    /// </para>
    /// </summary>
    [HttpGet("confirmed-orders")]
    public async Task<IActionResult> GetConfirmedOrders()
    {
        var orders = await _deliveryService.GetConfirmedPurchaseOrdersAsync();
        return Ok(orders.Select(po => PurchaseOrderProjection.ToReceivingDto(po)).ToList());
    }

    /// <summary>
    /// All recorded deliveries with their lines.
    /// Returns a purpose-built projection rather than the <see cref="Delivery"/>
    /// entity graph, which would otherwise carry the linked purchase order's
    /// <c>TotalAmount</c> and per-line prices to non-procurement roles.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var deliveries = await _deliveryService.GetDeliveriesAsync();
        return Ok(deliveries.Select(Project).ToList());
    }

    [HttpPost]
    [Authorize(Policy = Policies.SiteOperationsOnly)]
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

    /// <summary>
    /// Runs the DeliveryDiscrepancyAgent over one recorded delivery and returns
    /// its structured analysis, so Component 3 visibly demonstrates agent 3
    /// rather than asserting "analysis completed" in hardcoded UI copy.
    /// <para>
    /// The agent is advisory: nothing here changes the persisted delivery status.
    /// That was already decided when the delivery was recorded, and the recorded
    /// status is what the UI shows next to this result.
    /// </para>
    /// </summary>
    [HttpPost("{id:int}/discrepancy-analysis")]
    public async Task<IActionResult> AnalyzeDiscrepancy(int id)
    {
        // Items and the purchase order must be loaded: the ordered quantity is
        // read from PurchaseOrder.Items and the received/damaged totals from
        // Items. Without these includes the graph comes back empty and every
        // figure would silently report 0 — reporting "Fully verified" for a
        // delivery that actually had a 10-unit shortage.
        var delivery = await _dbContext.Deliveries
            .AsNoTracking()
            .Include(d => d.Items)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Items)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (delivery is null) return NotFound(new { message = $"Delivery #{id} not found." });

        var ordered = delivery.Items.Sum(item => OrderedQuantityFor(item, delivery));
        var received = delivery.Items.Sum(item => item.ReceivedQuantity);
        var damaged = delivery.Items.Sum(item => item.DamagedQuantity);

        var analysis = await _agentClient.AnalyzeDiscrepancyAsync(ordered, received, damaged);

        return Ok(new
        {
            DeliveryId = delivery.Id,
            Agent = "DeliveryDiscrepancyAgent",
            Tool = "analyze_discrepancy",
            ExecutionSource = analysis.ExecutionSource,
            OrderedQuantity = ordered,
            ReceivedQuantity = received,
            DamagedQuantity = damaged,
            // Shortage is derived here, not returned by the agent, so the number
            // shown to a user always matches the quantities displayed beside it.
            ShortageQuantity = Math.Max(0m, ordered - received),
            ShortageDetected = analysis.Output.ShortageDetected,
            DamageDetected = analysis.Output.DamageDetected,
            Summary = analysis.Output.Summary,
            Recommendation = BuildRecommendation(analysis.Output, ordered, received, damaged),
            DeliveryStatus = delivery.Status.ToString(),
            AnalyzedAtUtc = DateTime.UtcNow
        });
    }

    private static string BuildRecommendation(
        DeliveryAgentResult result,
        decimal ordered,
        decimal received,
        decimal damaged)
    {
        // An over-receipt cannot be reported as a shortage (ordered - received is
        // negative), but it is still a real finding: the delivery agent only
        // reasons about shortage and damage, so without this the recommendation
        // would describe a 500-vs-250 over-delivery as "50 units damaged" and
        // say nothing about the impossible quantity.
        var overReceived = received - ordered;
        var hasOverReceipt = overReceived > 0m;

        if (!result.ShortageDetected && !result.DamageDetected && !hasOverReceipt)
            return "No discrepancy. Proceed with standard receiving and quality inspection.";

        var parts = new List<string>();
        if (result.ShortageDetected) parts.Add($"short of {Math.Max(0m, ordered - received):0.##} unit(s)");
        if (result.DamageDetected) parts.Add($"{damaged:0.##} unit(s) reported damaged");
        if (hasOverReceipt) parts.Add($"over-received by {overReceived:0.##} unit(s) against the ordered quantity");

        return $"Review this delivery and the supplier response: {string.Join(" and ", parts)}.";
    }

    [HttpGet("{id:int}/issues")]
    public async Task<IActionResult> GetIssues(int id)
    {
        var issues = await _dbContext.DeliveryIssues
            .Where(issue => issue.DeliveryId == id)
            .OrderBy(issue => issue.ReportedAt)
            .Select(issue => new
            {
                issue.Id,
                issue.DeliveryId,
                issue.DeliveryItemId,
                IssueType = issue.IssueType.ToString(),
                issue.Description,
                Severity = issue.Severity.ToString(),
                issue.Status,
                issue.Resolution,
                issue.ReportedByUserId,
                issue.ReportedAt,
                issue.ResolvedAt
            })
            .ToListAsync();
        return Ok(issues);
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

    /// <summary>
    /// Explicit delivery projection. Carries only the operational fields needed to
    /// receive and reconcile material — never the linked purchase order's price
    /// or total. <c>ReferenceHandler.IgnoreCycles</c> previously made the raw
    /// entity serialise "successfully", which is exactly how the commercial data
    /// leak went unnoticed.
    /// </summary>
    private static object Project(Delivery d) => new
    {
        d.Id,
        d.PurchaseOrderId,
        SupplierName = d.PurchaseOrder?.Supplier?.Name ?? d.PurchaseOrder?.Quotation?.Supplier?.Name,
        ProjectName = d.PurchaseOrder?.Project?.Name,
        d.DeliveryReference,
        Status = d.Status.ToString(),
        d.DeliveredAt,
        d.ReceivedByUserId,
        Items = d.Items.Select(di => new
        {
            di.Id,
            di.MaterialId,
            MaterialName = di.Material?.Name,
            MaterialUnit = di.Material?.Unit,
            // Ordered quantity is required to compute a shortage (ordered vs
            // received) and to run the discrepancy agent. It comes from the
            // linked purchase order, never the delivery item — DeliveryItem only
            // records what actually arrived.
            OrderedQuantity = OrderedQuantityFor(di, d),
            di.ReceivedQuantity,
            di.DamagedQuantity
        }).ToList()
    };

    /// <summary>
    /// Resolves the ordered quantity for a delivery line by matching it to the
    /// purchase-order line for the same material. Returns 0 when the order line
    /// cannot be resolved, so the caller shows "0 ordered" rather than inventing
    /// a number; a shortage is then judged from received alone.
    /// </summary>
    private static decimal OrderedQuantityFor(DeliveryItem item, Delivery delivery)
    {
        var orderItem = delivery.PurchaseOrder?.Items?
            .FirstOrDefault(po => po.MaterialId == item.MaterialId);
        return orderItem?.OrderedQuantity ?? 0m;
    }
}
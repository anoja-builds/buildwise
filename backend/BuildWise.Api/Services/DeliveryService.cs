using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using DeliveryStatusEnum = BuildWise.Api.Models.Enums.DeliveryStatus;

namespace BuildWise.Api.Services;

public class DeliveryService
{
    private readonly ApplicationDbContext _context;
    private readonly OperationalAgentClient? _agentClient;
    private readonly OperationalAgentAuditService? _auditService;
    private readonly DeliveryAgentService? _deliveryRiskAgent;
    private readonly ILogger<DeliveryService> _logger;

    public DeliveryService(
        ApplicationDbContext context,
        OperationalAgentClient? agentClient = null,
        OperationalAgentAuditService? auditService = null,
        DeliveryAgentService? deliveryRiskAgent = null,
        ILogger<DeliveryService>? logger = null)
    {
        _context = context;
        _agentClient = agentClient;
        _auditService = auditService;
        _deliveryRiskAgent = deliveryRiskAgent;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeliveryService>.Instance;
    }

    public async Task<List<PurchaseOrder>> GetConfirmedPurchaseOrdersAsync()
    {
        return await _context.PurchaseOrders
            .Include(p => p.Items)
                .ThenInclude(i => i.Material)
            .Where(p => p.Status == PurchaseOrderStatus.Confirmed)
            .ToListAsync();
    }

    public async Task<Delivery> RecordDeliveryAsync(Delivery delivery)
    {
        var po = await _context.PurchaseOrders
            .Include(p => p.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(p => p.Id == delivery.PurchaseOrderId);

        if (po == null || po.Status != PurchaseOrderStatus.Confirmed)
            throw new InvalidOperationException("Deliveries can only be recorded for Confirmed Purchase Orders.");

        if (delivery.Items == null || delivery.Items.Count == 0)
            throw new InvalidOperationException("A delivery must contain at least one received item.");

        bool hasDiscrepancy = false;
        var agentResults = new List<object>();
        var executionSources = new List<string>();
        foreach (var item in delivery.Items)
        {
            if (item.ReceivedQuantity < 0 || item.DamagedQuantity < 0)
                throw new InvalidOperationException("Received and damaged quantities cannot be negative.");

            if (item.DamagedQuantity > item.ReceivedQuantity)
                throw new InvalidOperationException("Damaged quantity cannot exceed received quantity.");

            var poItem = po.Items.FirstOrDefault(i => i.MaterialId == item.MaterialId);
            if (poItem == null)
                throw new InvalidOperationException($"Material ID {item.MaterialId} is not part of this Purchase Order.");

            var agentRun = _agentClient == null
                ? new AgentClientResult<DeliveryAgentResult>(
                    DeliveryAgentResult.Deterministic(poItem.OrderedQuantity, item.ReceivedQuantity, item.DamagedQuantity),
                    "DeterministicFallback")
                : await _agentClient.AnalyzeDiscrepancyAsync(poItem.OrderedQuantity, item.ReceivedQuantity, item.DamagedQuantity);
            var assessment = agentRun.Output;
            agentResults.Add(new { materialId = item.MaterialId, ordered = poItem.OrderedQuantity, received = item.ReceivedQuantity, damaged = item.DamagedQuantity, assessment });
            executionSources.Add(agentRun.ExecutionSource);

            if (assessment.ShortageDetected || assessment.DamageDetected)
            {
                hasDiscrepancy = true;
            }
        }

        delivery.Status = hasDiscrepancy ? DeliveryStatusEnum.DiscrepancyReported : DeliveryStatusEnum.Received;
        delivery.DeliveredAt = DateTime.UtcNow;

        _context.Deliveries.Add(delivery);
        await _context.SaveChangesAsync();

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(
                delivery.ReceivedByUserId,
                $"Reconcile delivery {delivery.DeliveryReference} against its confirmed purchase order.",
                "DeliveryDiscrepancyAgent",
                new[] { "Read confirmed PO baseline", "Call analyze_discrepancy for each line", "Re-derive shortage and damage flags" },
                new[] { "analyze_discrepancy" },
                agentResults,
                string.Join(",", executionSources.Distinct()),
                new { valid = true, discrepancyDetected = hasDiscrepancy, businessRule = "received<ordered or damaged>0" },
                purchaseOrderId: delivery.PurchaseOrderId,
                deliveryId: delivery.Id);
        }

        if (_deliveryRiskAgent is not null)
        {
            try
            {
                await _deliveryRiskAgent.EvaluateDeliveryRiskAsync(
                    delivery.PurchaseOrderId,
                    delivery.ReceivedByUserId,
                    delivery.Id);
            }
            catch (Exception ex)
            {
                // Risk assessment is advisory. The delivery transaction is already saved;
                // retain its status and record the agent's safe failure in its own workflow.
                _logger.LogWarning(ex, "Delivery saved for {DeliveryId}, but delivery-risk assessment failed safely.", delivery.Id);
            }
        }

        return delivery;
    }

    public async Task<List<Delivery>> GetDeliveriesAsync()
    {
        return await _context.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(p => p!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(p => p!.Quotation)
                    .ThenInclude(q => q!.Supplier)
            .Include(d => d.Items)
                .ThenInclude(i => i.Material)
            .OrderByDescending(d => d.DeliveredAt)
            .ToListAsync();
    }
}
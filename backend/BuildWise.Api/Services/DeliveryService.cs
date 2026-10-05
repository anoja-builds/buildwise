using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
            // C3 links the supplier directly, C2 via the winning quotation.
            // Both must be loaded for the receiving projection to name the supplier.
            .Include(p => p.Supplier)
            .Include(p => p.Quotation)
                .ThenInclude(q => q!.Supplier)
            .Include(p => p.Items)
                .ThenInclude(i => i.Material)
            .Include(p => p.Items)
                .ThenInclude(i => i.QuotationItem)
                    .ThenInclude(qi => qi.MaterialRequestItem)
                        .ThenInclude(mri => mri.Material)
            .Where(p => p.Status == PurchaseOrderStatus.Confirmed)
            .ToListAsync();
    }

    public async Task<Delivery> RecordDeliveryAsync(Delivery delivery)
    {
        // Advisory agent outcomes are collected during validation but written
        // after the transaction commits, so a slow agent call never holds a
        // database lock.
        var agentResults = new List<object>();
        var executionSources = new List<string>();

        // Concurrency: two receivers can log deliveries against the same purchase
        // order at the same time. Both would read the same "already received"
        // total, both pass the cumulative check, and the order ends up
        // over-received. A SERIALIZABLE transaction makes the second request wait
        // for the first to commit, then re-read the totals and fail the check.
        //
        // The transaction covers only validate-and-persist. The advisory agent
        // calls run after the commit so a slow agent never holds a database lock.
        //
        // The in-memory provider used by the unit tests has no transaction
        // support, so the transaction is only opened for a relational database.
        var useTransaction = _context.Database.IsRelational();
        IDbContextTransaction? transaction = useTransaction
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

        try
        {
            await ValidateAndPersistAsync(delivery, agentResults, executionSources);

            if (transaction is not null)
                await transaction.CommitAsync();
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        // Advisory work runs after the commit: the delivery is already durably
        // recorded, and a slow or failing agent must not hold a database lock.
        await RecordAdvisoryOutcomesAsync(delivery, agentResults, executionSources);

        return delivery;
    }

    private async Task ValidateAndPersistAsync(
        Delivery delivery,
        List<object> agentResults,
        List<string> executionSources)
    {
        var po = await _context.PurchaseOrders
            .Include(p => p.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(p => p.Id == delivery.PurchaseOrderId);

        if (po == null || po.Status != PurchaseOrderStatus.Confirmed)
            throw new InvalidOperationException("Deliveries can only be recorded for Confirmed Purchase Orders.");

        // A delivery is tracked by its reference on paperwork, packing slips and
        // quality records, so an empty one cannot be reconciled later. The entity
        // column is a plain string (nullable in the DB), so this has to be an
        // explicit rule rather than a [Required] attribute on the model.
        if (string.IsNullOrWhiteSpace(delivery.DeliveryReference))
            throw new InvalidOperationException("A delivery reference is required.");

        if (delivery.Items == null || delivery.Items.Count == 0)
            throw new InvalidOperationException("A delivery must contain at least one received item.");

        // Quantities already received against this purchase order by earlier
        // deliveries. PO lines are received across several deliveries (partial
        // deliveries are normal), so the per-line check further down only sees
        // THIS delivery. Without folding in the earlier ones a supplier could
        // deliver 250 against a 250 order twice and the API would accept both.
        //
        // The incoming material ids are extracted to a plain list first: the
        // entity being recorded is not yet tracked by EF, so referencing
        // delivery.Items inside the query would fail to translate to SQL.
        var incomingMaterialIds = delivery.Items.Select(i => i.MaterialId).Distinct().ToList();

        var alreadyReceived = (await _context.DeliveryItems
                .Where(item => incomingMaterialIds.Contains(item.MaterialId)
                               && _context.Deliveries.Any(d => d.Id == item.DeliveryId && d.PurchaseOrderId == delivery.PurchaseOrderId))
                .GroupBy(item => item.MaterialId)
                .Select(group => new { MaterialId = group.Key, Quantity = group.Sum(item => item.ReceivedQuantity) })
                .ToListAsync())
            .ToDictionary(x => x.MaterialId, x => x.Quantity);

        bool hasDiscrepancy = false;
        foreach (var item in delivery.Items)
        {
            if (item.ReceivedQuantity < 0 || item.DamagedQuantity < 0)
                throw new InvalidOperationException("Received and damaged quantities cannot be negative.");

            if (item.DamagedQuantity > item.ReceivedQuantity)
                throw new InvalidOperationException("Damaged quantity cannot exceed received quantity.");

            var poItem = po.Items.FirstOrDefault(i => i.MaterialId == item.MaterialId);
            if (poItem == null)
                throw new InvalidOperationException($"Material ID {item.MaterialId} is not part of this Purchase Order.");

            // A supplier cannot deliver more than was ordered. Without this the
            // API accepted e.g. 500 received against a 250-unit order and stored
            // it as an ordinary DiscrepancyReported delivery, which is neither a
            // shortage nor a plausible over-delivery. Combined with the checks
            // above, the valid relationship is 0 <= Damaged <= Received <= Ordered.
            if (item.ReceivedQuantity > poItem.OrderedQuantity)
                throw new InvalidOperationException(
                    $"Received quantity cannot exceed the ordered quantity. Ordered {poItem.OrderedQuantity:0.##}, received {item.ReceivedQuantity:0.##}.");

            // Cumulative guard across partial deliveries: this delivery plus every
            // earlier delivery for the same PO line must still fit inside the
            // ordered quantity.
            if (alreadyReceived.TryGetValue(item.MaterialId, out var previouslyReceived))
            {
                var cumulative = previouslyReceived + item.ReceivedQuantity;
                if (cumulative > poItem.OrderedQuantity)
                    throw new InvalidOperationException(
                        $"Cumulative received quantity cannot exceed the ordered quantity. Ordered {poItem.OrderedQuantity:0.##}, " +
                        $"already received {previouslyReceived:0.##}, this delivery {item.ReceivedQuantity:0.##} (total {cumulative:0.##}).");
            }

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

        foreach (var item in delivery.Items)
        {
            var poItem = po.Items.First(i => i.MaterialId == item.MaterialId);
            var shortage = Math.Max(0m, poItem.OrderedQuantity - item.ReceivedQuantity);
            var sentForInspection = Math.Max(0m, item.ReceivedQuantity - item.DamagedQuantity);
            if (shortage > 0)
            {
                _context.DeliveryIssues.Add(new DeliveryIssue
                {
                    DeliveryId = delivery.Id,
                    DeliveryItemId = item.Id,
                    IssueType = DeliveryIssueType.Shortage,
                    Severity = DeliveryIssueSeverity.High,
                    Description = $"Shortage of {shortage} unit(s): ordered {poItem.OrderedQuantity}, received {item.ReceivedQuantity}. Sent for inspection: {sentForInspection}.",
                    ReportedByUserId = delivery.ReceivedByUserId
                });
            }
            if (item.DamagedQuantity > 0)
            {
                _context.DeliveryIssues.Add(new DeliveryIssue
                {
                    DeliveryId = delivery.Id,
                    DeliveryItemId = item.Id,
                    IssueType = DeliveryIssueType.Damage,
                    Severity = DeliveryIssueSeverity.Medium,
                    Description = $"Damage of {item.DamagedQuantity} unit(s) recorded. Sent for inspection: {sentForInspection}.",
                    ReportedByUserId = delivery.ReceivedByUserId
                });
            }
        }
        await _context.SaveChangesAsync();

        // Carry the discrepancy verdict out so the advisory audit records it.
        AgentDiscrepancyDetected = hasDiscrepancy;
    }

    /// <summary>Set by <see cref="ValidateAndPersistAsync"/> so the post-commit
    /// audit can record whether a discrepancy was found.</summary>
    internal bool AgentDiscrepancyDetected { get; private set; }

    /// <summary>
    /// Post-commit, advisory work: the delivery audit trail and the delivery-risk
    /// agent. Neither affects whether the delivery was accepted, and neither may
    /// run while a database lock is held.
    /// </summary>
    private async Task RecordAdvisoryOutcomesAsync(
        Delivery delivery,
        List<object> agentResults,
        List<string> executionSources)
    {
        var hasDiscrepancy = AgentDiscrepancyDetected;

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
    }

    public async Task<List<Delivery>> GetDeliveriesAsync()
    {
        return await _context.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(p => p!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(p => p!.Quotation)
                    .ThenInclude(q => q!.Supplier)
            // Purchase-order lines are needed to resolve each delivery item's
            // ordered quantity. Without this the receiving projection reported
            // 0 ordered, so a shortage could never be computed.
            .Include(d => d.PurchaseOrder)
                .ThenInclude(p => p!.Items)
            .Include(d => d.Items)
                .ThenInclude(i => i.Material)
            .OrderByDescending(d => d.DeliveredAt)
            .ToListAsync();
    }
}
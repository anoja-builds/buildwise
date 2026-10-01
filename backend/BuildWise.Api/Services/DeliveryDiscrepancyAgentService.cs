using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

/// <summary>
/// Delivery Discrepancy Agent — a genuinely distinct agent from the Delivery Risk Agent.
///
/// Risk Agent (existing): evaluates supplier timing risk BEFORE receiving.
/// Discrepancy Agent (this): analyzes ACTUAL receiving discrepancies AFTER quantities are recorded.
///
/// Read-only tools: this agent never changes PO quantities, approves discrepancies, or accepts damaged goods.
/// </summary>
public class DeliveryDiscrepancyAgentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDeliveryDiscrepancyAgentClient? _agentClient;

    public DeliveryDiscrepancyAgentService(ApplicationDbContext dbContext, IDeliveryDiscrepancyAgentClient? agentClient = null)
    {
        _dbContext = dbContext;
        _agentClient = agentClient;
    }

    /// <summary>
    /// Analyze receiving discrepancies for a specific delivery that has been received.
    /// Persists a full agent workflow with steps, tool results, and validation.
    /// </summary>
    public async Task<DiscrepancyAnalysisResult> AnalyzeDiscrepanciesAsync(int deliveryId, int initiatedByUserId)
    {
        // 1. Create the agent workflow record
        var workflow = new AgentWorkflow
        {
            DeliveryId = deliveryId,
            InitiatedByUserId = initiatedByUserId,
            Objective = $"Analyze receiving discrepancies for Delivery #{deliveryId}",
            Status = WorkflowStatus.Running,
            ApprovalStatus = AgentApprovalStatus.Pending,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.AgentWorkflows.Add(workflow);
        await _dbContext.SaveChangesAsync();

        AgentWorkflowStep? currentStep = null;
        try
        {
            // Step 1: Data Retrieval (Controlled read-only tool)
            var step1 = currentStep = await CreateStep(workflow.Id, "Data Retrieval",
                "Retrieve delivery, PO items, prior receipts, and damage records", 1);
            var deliveryData = await RetrieveDeliveryDataTool(deliveryId);
            await CompleteStep(step1, deliveryData);

            // All arithmetic is authoritative and validated before evidence crosses the service boundary.
            var step2 = currentStep = await CreateStep(workflow.Id, "Discrepancy Analysis",
                "Validate receiving arithmetic using shared fulfilment rules", 2);
            var analysis = PerformDiscrepancyAnalysis(deliveryData);
            var validation = ValidateAnalysis(analysis);
            if (!validation.IsValid) throw new InvalidOperationException("Invalid delivery quantity evidence.");
            await CompleteStep(step2, analysis);

            var step3 = currentStep = await CreateStep(workflow.Id, "Advisory Recommendations",
                "Run bounded delivery-discrepancy agent; use labelled fallback on failure", 3);
            var evidence = await CollectAgentEvidence(deliveryData, analysis);
            // Persist only bounded authoritative evidence, never raw model messages.
            step3.StructuredResult = JsonSerializer.Serialize(new { evidence });
            await _dbContext.SaveChangesAsync();
            DeliveryAgentAdvisory? advisory = null;
            var execution = new DeliveryAgentExecution("DeterministicFallback", null, 0, [], "not_configured");
            if (_agentClient != null)
            {
                try
                {
                    var response = await _agentClient.AnalyseAsync(evidence);
                    DeliveryAgentValidator.Validate(response, evidence);
                    advisory = response.Success ? response.Recommendation : null;
                    execution = new(response.Success ? "AgenticAI" : "DeterministicFallback",
                        response.ModelIdentifier, response.IterationCount, response.Trace, response.ErrorCode);
                }
                catch (Exception ex)
                {
                    // Provider exceptions may contain credentials or untrusted output. Store a fixed code only.
                    execution = new("DeterministicFallback", null, 0, [], ex switch
                    {
                        OperationCanceledException => "timeout",
                        JsonException => "invalid_output",
                        InvalidOperationException => "not_configured",
                        _ => "service_unavailable"
                    });
                }
            }
            var recommendations = advisory == null ? GenerateRecommendations(analysis)
                : new List<DiscrepancyRecommendation>
                {
                    new()
                    {
                        Category = "DeliveryRisk", MaterialName = "Delivery", Severity = advisory.RiskLevel,
                        Description = advisory.Summary,
                        Advisory = advisory.Summary + (advisory.LikelyCauses.Count == 0 ? "" :
                            " Possible causes (unconfirmed): " + string.Join("; ", advisory.LikelyCauses))
                            + " Recommended follow-up: " + string.Join("; ", advisory.RecommendedActions),
                        IsActionRequired = advisory.SupplierFollowUpRequired
                    }
                };
            await CompleteStep(step3, new { evidence, execution, advisory, recommendations });

            var step4 = currentStep = await CreateStep(workflow.Id, "Result Validation",
                "Record arithmetic and advisory validation outcome", 4);
            await CompleteStep(step4, new { validation.IsValid, validation.Errors,
                advisoryValidated = advisory != null, execution.Mode, execution.FallbackReason }, isValidation: true);

            // Build final result
            var result = new DiscrepancyAnalysisResult
            {
                WorkflowId = workflow.Id,
                DeliveryId = deliveryId,
                PurchaseOrderId = deliveryData.PurchaseOrderId,
                SupplierName = deliveryData.SupplierName,
                DeliveryReference = deliveryData.DeliveryReference,
                AnalyzedAt = DateTime.UtcNow,
                HasDiscrepancies = analysis.Items.Any(i => i.HasDiscrepancy),
                TotalShortage = analysis.Items.Sum(i => i.ShortageQuantity),
                TotalDamaged = analysis.Items.Sum(i => i.DamagedQuantity),
                TotalUndamagedReceived = analysis.Items.Sum(i => i.UndamagedReceivedQuantity),
                Items = analysis.Items,
                Recommendations = recommendations,
                Validation = validation,
                ExecutionMode = execution.Mode,
                Advisory = advisory,
                Execution = execution
            };

            // Finalize workflow
            workflow.Status = WorkflowStatus.Completed;
            workflow.FinalOutcome = result.HasDiscrepancies
                ? $"Discrepancies found: Shortage={result.TotalShortage}, Damaged={result.TotalDamaged}"
                : "No discrepancies — all quantities match purchase order.";
            workflow.FinalOutcome += $" Mode: {execution.Mode}.";
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return result;
        }
        catch (Exception)
        {
            workflow.Status = WorkflowStatus.Failed;
            workflow.FinalOutcome = "Delivery discrepancy analysis failed. No business action was executed.";
            if (currentStep != null)
            {
                currentStep.Status = WorkflowStepStatus.Failed;
                currentStep.ErrorMessage = "Delivery discrepancy step failed.";
                currentStep.CompletedAt = currentStep.UpdatedAt = DateTime.UtcNow;
            }
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Retrieve workflow history for a delivery's discrepancy analyses.
    /// </summary>
    public async Task<List<DiscrepancyWorkflowSummary>> GetWorkflowHistoryAsync(int deliveryId)
    {
        var workflows = await _dbContext.AgentWorkflows
            .Include(w => w.Steps)
            .Where(w => w.DeliveryId == deliveryId
                && w.Objective.ToLower().Contains("discrepanc"))
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        return workflows.Select(w => new DiscrepancyWorkflowSummary
        {
            WorkflowId = w.Id,
            DeliveryId = deliveryId,
            Status = w.Status.ToString(),
            CreatedAt = w.CreatedAt,
            ExecutionMode = ReadExecutionMode(w),
            Objective = w.Objective,
            FinalOutcome = w.FinalOutcome,
            StartedAt = w.StartedAt,
            CompletedAt = w.CompletedAt,
            Steps = w.Steps.OrderBy(s => s.StepOrder).Select(s => new DiscrepancyWorkflowStepSummary
            {
                StepName = s.StepName,
                AgentRole = s.AgentRole,
                Status = s.Status.ToString(),
                StepOrder = s.StepOrder,
                StructuredResult = s.StructuredResult,
                ValidationResult = s.ValidationResult,
                ErrorMessage = s.ErrorMessage,
                StartedAt = s.StartedAt,
                CompletedAt = s.CompletedAt
            }).ToList()
        }).ToList();
    }

    // ──────── Controlled Read-Only Tools ────────

    /// <summary>
    /// Tool 1: Retrieve delivery data including PO items, prior receipts, and damage.
    /// This is a read-only tool — it queries but never modifies data.
    /// </summary>
    private async Task<DeliveryDataSnapshot> RetrieveDeliveryDataTool(int deliveryId)
    {
        var delivery = await _dbContext.Deliveries
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
            .Include(d => d.PurchaseOrder)
                .ThenInclude(po => po!.Items)
                    .ThenInclude(poi => poi.Material)
            .Include(d => d.PurchaseOrder).ThenInclude(po => po!.Quotation).ThenInclude(q => q!.Supplier)
            .Include(d => d.Items).ThenInclude(di => di.PurchaseOrderItem)
                .ThenInclude(poi => poi!.QuotationItem).ThenInclude(qi => qi!.MaterialRequestItem).ThenInclude(ri => ri.Material)
            .Include(d => d.Items)
                .ThenInclude(di => di.PurchaseOrderItem)
                    .ThenInclude(poi => poi!.Material)
            .Include(d => d.ReceivedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deliveryId)
            ?? throw new ArgumentException($"Delivery #{deliveryId} not found.");

        if (delivery.ReceivedAt == null)
            throw new InvalidOperationException("Delivery has not been received yet. Record quantities first.");

        if (delivery.Items.Count == 0 || delivery.Items.Select(i => i.PurchaseOrderItemId).Distinct().Count() != delivery.Items.Count
            || delivery.Items.Any(i => i.PurchaseOrderItem == null || i.PurchaseOrderItem.PurchaseOrderId != delivery.PurchaseOrderId))
            throw new InvalidOperationException("Invalid delivery item evidence.");

        // Receipt-time analysis: later receipts must never be treated as prior receipts.
        var priorDeliveries = await _dbContext.DeliveryItems.AsNoTracking()
            .Where(i => i.DeliveryId != deliveryId
                && i.Delivery!.PurchaseOrderId == delivery.PurchaseOrderId
                && (i.Delivery.ReceivedAt < delivery.ReceivedAt
                    || (i.Delivery.ReceivedAt == delivery.ReceivedAt && i.DeliveryId < deliveryId))
                && (i.Delivery.Status == DeliveryStatus.Received
                    || i.Delivery.Status == DeliveryStatus.PartiallyReceived
                    || i.Delivery.Status == DeliveryStatus.DiscrepancyReported))
            .ToListAsync();

        var priorByItem = priorDeliveries
            .GroupBy(i => i.PurchaseOrderItemId)
            .ToDictionary(g => g.Key, g => new PriorReceiptSummary
            {
                TotalPriorReceived = g.Sum(i => i.ReceivedQuantity),
                TotalPriorDamaged = g.Sum(i => i.DamagedQuantity),
                DeliveryCount = g.Select(i => i.DeliveryId).Distinct().Count()
            });

        return new DeliveryDataSnapshot
        {
            DeliveryId = delivery.Id,
            PurchaseOrderId = delivery.PurchaseOrderId,
            SupplierId = delivery.PurchaseOrder?.QuotationId != null ? delivery.PurchaseOrder.Quotation?.SupplierId : delivery.PurchaseOrder?.SupplierId,
            SupplierName = (delivery.PurchaseOrder?.QuotationId != null ? delivery.PurchaseOrder.Quotation?.Supplier?.Name : delivery.PurchaseOrder?.Supplier?.Name) ?? "Unknown",
            DeliveryReference = delivery.DeliveryReference ?? $"DEL-{delivery.Id}",
            DeliveryStatus = delivery.Status.ToString(),
            ReceivedAt = delivery.ReceivedAt,
            ReceivedBy = delivery.ReceivedByUser?.FullName ?? "Unknown",
            Items = delivery.Items.Select(di => new DeliveryItemSnapshot
            {
                PurchaseOrderItemId = di.PurchaseOrderItemId,
                MaterialName = (di.PurchaseOrderItem?.QuotationItemId != null ? di.PurchaseOrderItem.QuotationItem?.MaterialRequestItem?.Material?.Name : di.PurchaseOrderItem?.Material?.Name) ?? "Unknown",
                MaterialUnit = (di.PurchaseOrderItem?.QuotationItemId != null ? di.PurchaseOrderItem.QuotationItem?.MaterialRequestItem?.Material?.Unit : di.PurchaseOrderItem?.Material?.Unit) ?? "units",
                OrderedQuantity = di.PurchaseOrderItem?.OrderedQuantity ?? 0,
                NewlyReceivedQuantity = di.ReceivedQuantity,
                NewlyDamagedQuantity = di.DamagedQuantity,
                PriorReceivedQuantity = priorByItem.GetValueOrDefault(di.PurchaseOrderItemId)?.TotalPriorReceived ?? 0,
                PriorDamagedQuantity = priorByItem.GetValueOrDefault(di.PurchaseOrderItemId)?.TotalPriorDamaged ?? 0,
                PriorDeliveryCount = priorByItem.GetValueOrDefault(di.PurchaseOrderItemId)?.DeliveryCount ?? 0
            }).ToList()
        };
    }

    // ──────── Deterministic Analysis ────────

    /// <summary>
    /// Tool 2: Perform the actual discrepancy analysis — factual comparison of quantities.
    /// </summary>
    private static DiscrepancyAnalysis PerformDiscrepancyAnalysis(DeliveryDataSnapshot data)
    {
        var items = data.Items.Select(item =>
        {
            var totalReceivedAllDeliveries = item.PriorReceivedQuantity + item.NewlyReceivedQuantity;
            var totalDamagedAllDeliveries = item.PriorDamagedQuantity + item.NewlyDamagedQuantity;
            var quantities = DeliveryQuantityRules.Calculate(item.OrderedQuantity, item.PriorReceivedQuantity,
                item.PriorDamagedQuantity, item.NewlyReceivedQuantity, item.NewlyDamagedQuantity);
            var shortage = quantities.PhysicalShortage;
            var undamagedThisDelivery = quantities.CurrentUndamaged;

            var flags = new List<string>();
            if (shortage > 0) flags.Add("Shortage");
            if (item.NewlyDamagedQuantity > 0) flags.Add("Damage");
            if (quantities.OverDelivery)
                flags.Add("OverDelivery");
            if (item.NewlyReceivedQuantity == 0 && quantities.OutstandingBefore > 0)
                flags.Add("NothingReceived");

            return new DiscrepancyItemAnalysis
            {
                PurchaseOrderItemId = item.PurchaseOrderItemId,
                PreviouslyFulfilledQuantity = quantities.PriorFulfilled,
                TotalFulfilledQuantity = quantities.TotalFulfilled,
                OutstandingBeforeReceipt = quantities.OutstandingBefore,
                OutstandingAfterReceipt = quantities.OutstandingAfter,
                MaterialName = item.MaterialName,
                MaterialUnit = item.MaterialUnit,
                OrderedQuantity = item.OrderedQuantity,
                PreviouslyReceivedQuantity = item.PriorReceivedQuantity,
                PreviouslyDamagedQuantity = item.PriorDamagedQuantity,
                NewlyReceivedQuantity = item.NewlyReceivedQuantity,
                DamagedQuantity = item.NewlyDamagedQuantity,
                UndamagedReceivedQuantity = undamagedThisDelivery,
                TotalReceivedToDate = totalReceivedAllDeliveries,
                TotalDamagedToDate = totalDamagedAllDeliveries,
                ShortageQuantity = Math.Max(0, shortage),
                HasDiscrepancy = flags.Count > 0,
                DiscrepancyFlags = flags
            };
        }).ToList();

        return new DiscrepancyAnalysis { Items = items };
    }

    /// <summary>
    /// Tool 3: Generate advisory recommendations. Read-only — never modifies PO or delivery data.
    /// </summary>
    private static List<DiscrepancyRecommendation> GenerateRecommendations(DiscrepancyAnalysis analysis)
    {
        var recommendations = new List<DiscrepancyRecommendation>();

        foreach (var item in analysis.Items.Where(i => i.HasDiscrepancy))
        {
            if (item.DiscrepancyFlags.Contains("Shortage"))
            {
                recommendations.Add(new DiscrepancyRecommendation
                {
                    Category = "Shortage",
                    MaterialName = item.MaterialName,
                    Severity = item.ShortageQuantity > item.OrderedQuantity * 0.1m ? "High" : "Medium",
                    Description = $"Shortage of {item.ShortageQuantity} {item.MaterialUnit} detected " +
                        $"(outstanding before receipt {item.OutstandingBeforeReceipt}, received now {item.NewlyReceivedQuantity}).",
                    Advisory = "Notify procurement team. Consider scheduling a follow-up delivery or " +
                        "contacting the supplier for the outstanding quantity.",
                    IsActionRequired = true
                });
            }

            if (item.DiscrepancyFlags.Contains("Damage"))
            {
                recommendations.Add(new DiscrepancyRecommendation
                {
                    Category = "Damage",
                    MaterialName = item.MaterialName,
                    Severity = item.DamagedQuantity > item.NewlyReceivedQuantity * 0.1m ? "High" : "Medium",
                    Description = $"{item.DamagedQuantity} {item.MaterialUnit} of {item.MaterialName} arrived damaged " +
                        $"out of {item.NewlyReceivedQuantity} received.",
                    Advisory = "Document damage with photographic evidence. Initiate quality inspection. " +
                        "Consider filing a claim with the supplier for damaged goods.",
                    IsActionRequired = true
                });
            }

            if (item.DiscrepancyFlags.Contains("OverDelivery"))
            {
                recommendations.Add(new DiscrepancyRecommendation
                {
                    Category = "OverDelivery",
                    MaterialName = item.MaterialName,
                    Severity = "Low",
                    Description = $"More {item.MaterialUnit} received than the outstanding ordered quantity.",
                    Advisory = "Verify quantities with the supplier. Excess materials may need to be returned " +
                        "or reconciled against future orders.",
                    IsActionRequired = false
                });
            }
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new DiscrepancyRecommendation
            {
                Category = "NoIssues",
                MaterialName = "All Items",
                Severity = "None",
                Description = "All received quantities match purchase order expectations.",
                Advisory = "Delivery is compliant. Proceed with quality inspection.",
                IsActionRequired = false
            });
        }

        return recommendations;
    }

    /// <summary>
    /// Tool 4: Validate internal consistency of the analysis output.
    /// </summary>
    private static DiscrepancyValidation ValidateAnalysis(
        DiscrepancyAnalysis analysis)
    {
        var errors = new List<string>();

        foreach (var item in analysis.Items)
        {
            var q = DeliveryQuantityRules.Calculate(item.OrderedQuantity, item.PreviouslyReceivedQuantity,
                item.PreviouslyDamagedQuantity, item.NewlyReceivedQuantity, item.DamagedQuantity);
            if (item.OrderedQuantity < 0 || item.NewlyReceivedQuantity < 0 || item.DamagedQuantity < 0
                || item.PreviouslyReceivedQuantity < 0 || item.PreviouslyDamagedQuantity < 0
                || item.PreviouslyDamagedQuantity > item.PreviouslyReceivedQuantity
                || item.PreviouslyFulfilledQuantity != q.PriorFulfilled || item.TotalFulfilledQuantity != q.TotalFulfilled
                || item.OutstandingBeforeReceipt != q.OutstandingBefore || item.OutstandingAfterReceipt != q.OutstandingAfter
                || item.ShortageQuantity != q.PhysicalShortage || item.UndamagedReceivedQuantity != q.CurrentUndamaged
                || item.DiscrepancyFlags.Contains("OverDelivery") != q.OverDelivery)
                errors.Add($"{item.MaterialName}: invalid fulfilment arithmetic.");

            if (item.DamagedQuantity > item.NewlyReceivedQuantity)
                errors.Add($"{item.MaterialName}: damaged ({item.DamagedQuantity}) exceeds received ({item.NewlyReceivedQuantity}).");

            if (item.UndamagedReceivedQuantity < 0)
                errors.Add($"{item.MaterialName}: undamaged received quantity is negative.");

            if (item.ShortageQuantity < 0)
                errors.Add($"{item.MaterialName}: shortage quantity is negative.");
        }

        return new DiscrepancyValidation
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Timestamp = DateTime.UtcNow
        };
    }

    // ──────── Helper Methods ────────

    private async Task<DeliveryAgentEvidence> CollectAgentEvidence(DeliveryDataSnapshot data, DiscrepancyAnalysis analysis)
    {
        var history = _dbContext.Deliveries.AsNoTracking().Where(d => data.SupplierId != null
            && (d.PurchaseOrder!.QuotationId != null ? d.PurchaseOrder.Quotation!.SupplierId : d.PurchaseOrder.SupplierId) == data.SupplierId
            && (d.ReceivedAt < data.ReceivedAt || (d.ReceivedAt == data.ReceivedAt && d.Id < data.DeliveryId))
            && (d.Status == DeliveryStatus.Received || d.Status == DeliveryStatus.PartiallyReceived || d.Status == DeliveryStatus.DiscrepancyReported));
        var rows = await history.OrderByDescending(d => d.ReceivedAt).ThenByDescending(d => d.Id).Take(21)
            .Select(d => new DeliveryAgentHistory("delivery:" + d.Id, d.Id, d.ReceivedAt!.Value,
                d.Status.ToString(), d.Items.Count, d.Items.Count(i => i.DamagedQuantity > 0))).ToListAsync();
        var discrepancies = await history.Where(d => d.Status == DeliveryStatus.DiscrepancyReported || d.Status == DeliveryStatus.PartiallyReceived)
            .OrderByDescending(d => d.ReceivedAt).ThenByDescending(d => d.Id).Take(21)
            .Select(d => new DeliveryAgentHistory("delivery:" + d.Id, d.Id, d.ReceivedAt!.Value,
                d.Status.ToString(), d.Items.Count, d.Items.Count(i => i.DamagedQuantity > 0))).ToListAsync();
        var items = analysis.Items.Select(i => new DeliveryAgentItem($"po-item:{i.PurchaseOrderItemId}",
            Clip(i.MaterialName, 200), Clip(i.MaterialUnit, 50), i.OrderedQuantity, i.PreviouslyFulfilledQuantity,
            i.NewlyReceivedQuantity, i.DamagedQuantity, i.OutstandingBeforeReceipt, i.OutstandingAfterReceipt,
            i.ShortageQuantity, i.DiscrepancyFlags.Contains("OverDelivery"))).ToList();
        return new(data.DeliveryId, data.SupplierId, Clip(data.SupplierName, 200), data.ReceivedAt!.Value,
            items, rows.Take(20).ToList(), rows.Count > 20, discrepancies.Take(20).ToList(), discrepancies.Count > 20,
            new[] { $"delivery:{data.DeliveryId}" }.Concat(items.Select(i => i.EvidenceRef))
                .Concat(rows.Take(20).Select(i => i.EvidenceRef)).Concat(discrepancies.Take(20).Select(i => i.EvidenceRef)).Distinct().ToList());
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    private static string ReadExecutionMode(AgentWorkflow workflow)
    {
        var json = workflow.Steps.FirstOrDefault(s => s.StepOrder == 3)?.StructuredResult;
        if (json != null)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("execution", out var execution)
                && execution.TryGetProperty("Mode", out var mode))
                return mode.GetString() ?? "Unknown";
        }
        // Legacy successful runs had no provider path; do not mislabel them AgenticAI.
        return workflow.Status == WorkflowStatus.Completed ? "Deterministic" : "Unknown";
    }

    private async Task<AgentWorkflowStep> CreateStep(int workflowId, string role, string name, int order)
    {
        var step = new AgentWorkflowStep
        {
            AgentWorkflowId = workflowId,
            AgentRole = role,
            StepName = name,
            StepOrder = order,
            Status = WorkflowStepStatus.Running,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.AgentWorkflowSteps.Add(step);
        await _dbContext.SaveChangesAsync();
        return step;
    }

    private async Task CompleteStep(AgentWorkflowStep step, object result, bool isValidation = false)
    {
        step.Status = WorkflowStepStatus.Completed;
        step.CompletedAt = DateTime.UtcNow;
        step.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
        if (isValidation)
            step.ValidationResult = json;
        else
            step.StructuredResult = json;

        await _dbContext.SaveChangesAsync();
    }
}

// ──────── Data Transfer Models ────────

public class DeliveryDataSnapshot
{
    public int? SupplierId { get; set; }
    public int DeliveryId { get; set; }
    public int PurchaseOrderId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string DeliveryReference { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public DateTime? ReceivedAt { get; set; }
    public string ReceivedBy { get; set; } = string.Empty;
    public List<DeliveryItemSnapshot> Items { get; set; } = new();
}

public class DeliveryItemSnapshot
{
    public int PurchaseOrderItemId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public string MaterialUnit { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal NewlyReceivedQuantity { get; set; }
    public decimal NewlyDamagedQuantity { get; set; }
    public decimal PriorReceivedQuantity { get; set; }
    public decimal PriorDamagedQuantity { get; set; }
    public int PriorDeliveryCount { get; set; }
}

public class PriorReceiptSummary
{
    public decimal TotalPriorReceived { get; set; }
    public decimal TotalPriorDamaged { get; set; }
    public int DeliveryCount { get; set; }
}

public class DiscrepancyAnalysis
{
    public List<DiscrepancyItemAnalysis> Items { get; set; } = new();
}

public class DiscrepancyItemAnalysis
{
    public int PurchaseOrderItemId { get; set; }
    public decimal PreviouslyFulfilledQuantity { get; set; }
    public decimal TotalFulfilledQuantity { get; set; }
    public decimal OutstandingBeforeReceipt { get; set; }
    public decimal OutstandingAfterReceipt { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public string MaterialUnit { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyReceivedQuantity { get; set; }
    public decimal PreviouslyDamagedQuantity { get; set; }
    public decimal NewlyReceivedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal UndamagedReceivedQuantity { get; set; }
    public decimal TotalReceivedToDate { get; set; }
    public decimal TotalDamagedToDate { get; set; }
    public decimal ShortageQuantity { get; set; }
    public bool HasDiscrepancy { get; set; }
    public List<string> DiscrepancyFlags { get; set; } = new();
}

public class DiscrepancyRecommendation
{
    public string Category { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Advisory { get; set; } = string.Empty;
    public bool IsActionRequired { get; set; }
}

public class DiscrepancyValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class DiscrepancyAnalysisResult
{
    public int WorkflowId { get; set; }
    public int DeliveryId { get; set; }
    public int PurchaseOrderId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string DeliveryReference { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }
    public bool HasDiscrepancies { get; set; }
    public decimal TotalShortage { get; set; }
    public decimal TotalDamaged { get; set; }
    public decimal TotalUndamagedReceived { get; set; }
    public List<DiscrepancyItemAnalysis> Items { get; set; } = new();
    public List<DiscrepancyRecommendation> Recommendations { get; set; } = new();
    public DiscrepancyValidation Validation { get; set; } = new();
    public string ExecutionMode { get; set; } = "DeterministicFallback";
    public DeliveryAgentAdvisory? Advisory { get; set; }
    public DeliveryAgentExecution? Execution { get; set; }
}

public class DiscrepancyWorkflowSummary
{
    public DateTime CreatedAt { get; set; }
    public string ExecutionMode { get; set; } = string.Empty;
    public int WorkflowId { get; set; }
    public int DeliveryId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string? FinalOutcome { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<DiscrepancyWorkflowStepSummary> Steps { get; set; } = new();
}

public class DiscrepancyWorkflowStepSummary
{
    public string StepName { get; set; } = string.Empty;
    public string AgentRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public string? StructuredResult { get; set; }
    public string? ValidationResult { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

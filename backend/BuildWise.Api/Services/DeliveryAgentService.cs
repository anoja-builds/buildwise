using System.Text;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

/// <summary>Auditable delivery-risk workflow with deterministic safe fallback.</summary>
public class DeliveryAgentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DeliveryAgentService> _logger;

    public DeliveryAgentService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<DeliveryAgentService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<DeliveryRiskAssessment> EvaluateDeliveryRiskAsync(
        int purchaseOrderId,
        int? userId = null,
        int? deliveryId = null)
    {
        var workflow = await CreateWorkflowRecordAsync(purchaseOrderId, userId, deliveryId);
        AgentWorkflowStep? activeStep = null;
        try
        {
            activeStep = await StartStepAsync(workflow.Id, "DeliveryDataRetrievalAgent",
                "Retrieve purchase order, supplier, and historical delivery facts", 1);
            var systemData = await GetSystemDataToolAsync(purchaseOrderId);
            await CompleteStepAsync(activeStep, systemData);

            activeStep = await StartStepAsync(workflow.Id, "DeliveryRiskAnalysisAgent",
                "Interpret delivery facts and determine risk level", 2);
            var assessment = await PerformRiskAnalysisAsync(systemData);
            await CompleteStepAsync(activeStep, assessment);

            activeStep = await StartStepAsync(workflow.Id, "DeliveryRiskValidationAgent",
                "Validate structured output and scoring consistency", 3);
            var validation = ValidateAssessment(assessment);
            await CompleteStepAsync(activeStep, validation, validation);
            if (!validation.Valid)
                throw new InvalidOperationException("Delivery agent returned an invalid structured assessment.");

            workflow.Status = WorkflowStatus.Completed;
            workflow.ApprovalStatus = AgentApprovalStatus.NotRequired;
            workflow.FinalOutcome = $"Risk assessment completed: {assessment.RiskLevel} (score {assessment.RiskScore}).";
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return assessment;
        }
        catch (Exception ex)
        {
            if (activeStep is not null)
            {
                activeStep.Status = WorkflowStepStatus.Failed;
                activeStep.ErrorMessage = ex.Message;
                activeStep.CompletedAt = DateTime.UtcNow;
                activeStep.UpdatedAt = DateTime.UtcNow;
            }
            workflow.Status = WorkflowStatus.Failed;
            workflow.FinalOutcome = "Delivery risk workflow failed safely.";
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            _logger.LogError(ex, "Delivery risk workflow failed for purchase order {PurchaseOrderId}.", purchaseOrderId);
            throw;
        }
    }

    private async Task<AgentWorkflow> CreateWorkflowRecordAsync(int purchaseOrderId, int? userId, int? deliveryId)
    {
        if (!await _dbContext.PurchaseOrders.AnyAsync(po => po.Id == purchaseOrderId))
            throw new ArgumentException($"Purchase order {purchaseOrderId} not found.", nameof(purchaseOrderId));

        var workflow = new AgentWorkflow
        {
            PurchaseOrderId = purchaseOrderId,
            DeliveryId = deliveryId,
            InitiatedByUserId = userId ?? 1,
            Objective = $"Determine delivery risk for Purchase Order #{purchaseOrderId}.",
            Status = WorkflowStatus.Running,
            ApprovalStatus = AgentApprovalStatus.Pending,
            StartedAt = DateTime.UtcNow
        };
        _dbContext.AgentWorkflows.Add(workflow);
        await _dbContext.SaveChangesAsync();
        return workflow;
    }

    private async Task<AgentWorkflowStep> StartStepAsync(int workflowId, string role, string name, int order)
    {
        var step = new AgentWorkflowStep
        {
            AgentWorkflowId = workflowId,
            AgentRole = role,
            StepName = name,
            StepOrder = order,
            Status = WorkflowStepStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _dbContext.AgentWorkflowSteps.Add(step);
        await _dbContext.SaveChangesAsync();
        return step;
    }

    private async Task CompleteStepAsync(AgentWorkflowStep step, object result, object? validationResult = null)
    {
        step.Status = WorkflowStepStatus.Completed;
        step.StructuredResult = JsonSerializer.Serialize(result);
        step.ValidationResult = validationResult is null
            ? null
            : JsonSerializer.Serialize(validationResult);
        step.CompletedAt = DateTime.UtcNow;
        step.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    // Controlled tool: reads only persisted purchase-order and delivery facts.
    private async Task<DeliveryRiskSystemData> GetSystemDataToolAsync(int purchaseOrderId)
    {
        var purchaseOrder = await _dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(po => po.Supplier)
            .Include(po => po.Items)
                .ThenInclude(item => item.Material)
            .FirstOrDefaultAsync(po => po.Id == purchaseOrderId)
            ?? throw new ArgumentException($"Purchase order {purchaseOrderId} not found.", nameof(purchaseOrderId));

        var supplierId = purchaseOrder.SupplierId;
        var query = _dbContext.Deliveries
            .AsNoTracking()
            .Where(d => d.Status != DeliveryStatus.Scheduled);
        if (supplierId.HasValue)
            query = query.Where(d => d.PurchaseOrder!.SupplierId == supplierId.Value);

        var history = await query.Include(d => d.PurchaseOrder).ToListAsync();
        var lateDeliveries = history.Count(d =>
            d.PurchaseOrder?.ExpectedDeliveryDate is DateOnly expected &&
            d.DeliveredAt.Date > expected.ToDateTime(TimeOnly.MinValue).Date);
        var discrepancyDeliveries = history.Count(d => d.Status == DeliveryStatus.DiscrepancyReported);
        var promisedDate = purchaseOrder.ExpectedDeliveryDate?.ToDateTime(TimeOnly.MinValue)
            ?? DateTime.UtcNow.AddDays(3);
        var requiredDate = purchaseOrder.OrderDate.ToDateTime(TimeOnly.MinValue).AddDays(5);

        return new DeliveryRiskSystemData(
            purchaseOrder.Id,
            supplierId ?? 0,
            purchaseOrder.Supplier?.Name ?? "Unknown",
            purchaseOrder.Supplier?.Status.ToString() ?? "Active",
            requiredDate,
            promisedDate,
            history.Count,
            lateDeliveries,
            discrepancyDeliveries,
            history.Count == 0 ? 100m : Math.Round((decimal)(history.Count - lateDeliveries) / history.Count * 100m, 2),
            history.Count == 0 ? 0m : Math.Round((decimal)discrepancyDeliveries / history.Count * 100m, 2),
            purchaseOrder.Items.Select(item => item.Material?.Name ?? $"Material #{item.MaterialId}").ToList());
    }

    private async Task<DeliveryRiskAssessment> PerformRiskAnalysisAsync(DeliveryRiskSystemData data)
    {
        var delayDays = (int)(data.PromisedDate - data.RequiredDate).TotalDays;
        var apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return PerformRuleBasedAssessment(data, delayDays);

        try
        {
            var model = _configuration["Gemini:Model"] ?? "gemini-2.0-flash";
            var prompt = JsonSerializer.Serialize(new
            {
                data.SupplierName,
                data.SupplierStatus,
                delay_days = delayDays,
                historical_on_time_rate = data.OnTimeRate,
                historical_discrepancy_rate = data.DiscrepancyRate,
                item_count = data.Items.Count
            });
            var request = new
            {
                contents = new[] { new { parts = new[] { new { text = "Interpret these delivery facts. Return only JSON with riskLevel, riskScore, delayDays, onTimeRate, reasons, warnings, recommendation. Facts: " + prompt } } } },
                generationConfig = new { responseMimeType = "application/json" }
            };
            var client = _httpClientFactory.CreateClient("Gemini");
            client.Timeout = TimeSpan.FromSeconds(8);
            var response = await client.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}",
                new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var text = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            var assessment = JsonSerializer.Deserialize<DeliveryRiskAssessment>(text!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (assessment is null) return PerformRuleBasedAssessment(data, delayDays);
            assessment.DelayDays = delayDays;
            assessment.OnTimeRate = data.OnTimeRate;
            return ValidateAssessment(assessment).Valid ? assessment : PerformRuleBasedAssessment(data, delayDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini delivery-risk analysis unavailable; using deterministic fallback.");
            return PerformRuleBasedAssessment(data, delayDays);
        }
    }

    private DeliveryRiskAssessment PerformRuleBasedAssessment(DeliveryRiskSystemData data, int delayDays)
    {
        var assessment = new DeliveryRiskAssessment
        {
            DelayDays = delayDays,
            OnTimeRate = data.OnTimeRate,
            DiscrepancyRate = data.DiscrepancyRate,
            TotalPastDeliveries = data.TotalPastDeliveries
        };
        if (delayDays > 2 || data.OnTimeRate < 70m || data.SupplierStatus != "Active")
        {
            assessment.RiskLevel = "High";
            assessment.RiskScore = 85;
            assessment.Recommendation = "Flag for manual procurement review.";
        }
        else if (delayDays > 0 || data.OnTimeRate < 90m || data.DiscrepancyRate > 20m)
        {
            assessment.RiskLevel = "Medium";
            assessment.RiskScore = 50;
            assessment.Recommendation = "Monitor delivery closely.";
        }
        else
        {
            assessment.RiskLevel = "Low";
            assessment.RiskScore = 15;
            assessment.Recommendation = "Proceed with standard receiving workflow.";
        }
        if (delayDays > 0) assessment.Reasons.Add($"Promised delivery is {delayDays} days after the required date.");
        if (data.OnTimeRate < 90m) assessment.Reasons.Add($"Historical on-time rate is {data.OnTimeRate:F1}%.");
        if (data.DiscrepancyRate > 0) assessment.Reasons.Add($"Historical discrepancy rate is {data.DiscrepancyRate:F1}%.");
        if (assessment.Reasons.Count == 0) assessment.Reasons.Add("Supplier history and promised date satisfy the deterministic rules.");
        return assessment;
    }

    private static DeliveryRiskValidation ValidateAssessment(DeliveryRiskAssessment assessment) => new(
        !string.IsNullOrWhiteSpace(assessment.RiskLevel) &&
        assessment.RiskLevel is "Low" or "Medium" or "High" &&
        assessment.RiskScore is >= 0 and <= 100,
        DateTime.UtcNow);
}

public sealed record DeliveryRiskSystemData(
    int PurchaseOrderId,
    int SupplierId,
    string SupplierName,
    string SupplierStatus,
    DateTime RequiredDate,
    DateTime PromisedDate,
    int TotalPastDeliveries,
    int LateDeliveries,
    int DiscrepancyDeliveries,
    decimal OnTimeRate,
    decimal DiscrepancyRate,
    List<string> Items);

public sealed class DeliveryRiskAssessment
{
    public string RiskLevel { get; set; } = "Low";
    public int RiskScore { get; set; }
    public int DelayDays { get; set; }
    public decimal OnTimeRate { get; set; }
    public decimal DiscrepancyRate { get; set; }
    public int TotalPastDeliveries { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;
}

public sealed record DeliveryRiskValidation(bool Valid, DateTime Timestamp);

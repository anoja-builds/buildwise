using System.Text;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class DeliveryRiskAgentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public DeliveryRiskAgentService(
        ApplicationDbContext dbContext,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _httpClient = new HttpClient();
    }

    public async Task<DeliveryRiskAssessment> EvaluateDeliveryRiskAsync(int purchaseOrderId, int? userId = null, int? deliveryId = null)
    {
        // 1. Initialize Agentic Workflow
        var workflow = await CreateWorkflowRecord(purchaseOrderId, userId, deliveryId);

        try
        {
            // 2. Controlled Tool: Get System Data
            var step1 = await StartStep(workflow.Id, "Data Retrieval", "Retrieve PO, Supplier, and Historical Performance Data", 1);
            var systemData = await GetSystemDataTool(purchaseOrderId);
            await CompleteStep(step1, systemData);

            // 3. Risk Reasoning (Factual Calculation + AI Interpretation)
            var step2 = await StartStep(workflow.Id, "Risk Analysis", "Reasoning over retrieved data to determine risk level", 2);
            var assessment = await PerformRiskAnalysis(systemData);
            await CompleteStep(step2, assessment);

            // 4. Output Validation
            var step3 = await StartStep(workflow.Id, "Result Validation", "Validate structured output and scoring consistency", 3);
            var validationResult = ValidateAssessment(assessment);
            await CompleteStep(step3, validationResult);

            // 5. Finalize Workflow
            workflow.Status = WorkflowStatus.Completed;
            workflow.FinalOutcome = $"Risk Assessment Completed: {assessment.RiskLevel} (Score: {assessment.RiskScore})";
            workflow.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return assessment;
        }
        catch (Exception ex)
        {
            workflow.Status = WorkflowStatus.Failed;
            workflow.FinalOutcome = $"Workflow Failed: {ex.Message}";
            workflow.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            throw;
        }
    }

    private async Task<AgentWorkflow> CreateWorkflowRecord(int poId, int? userId, int? deliveryId)
    {
        var workflow = new AgentWorkflow
        {
            PurchaseOrderId = poId,
            DeliveryId = deliveryId,
            InitiatedByUserId = userId,
            Objective = $"Determine delivery risk for Purchase Order #{poId}",
            Status = WorkflowStatus.Running,
            ApprovalStatus = AgentApprovalStatus.Pending,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AgentWorkflows.Add(workflow);
        await _dbContext.SaveChangesAsync();
        return workflow;
    }

    private async Task<AgentWorkflowStep> StartStep(int workflowId, string role, string name, int order)
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

    private async Task CompleteStep(AgentWorkflowStep step, object result)
    {
        step.Status = WorkflowStepStatus.Completed;
        step.CompletedAt = DateTime.UtcNow;
        step.StructuredResultJson = JsonSerializer.Serialize(result);
        await _dbContext.SaveChangesAsync();
    }

    // Controlled Tool 1: Data Retrieval
    private async Task<dynamic> GetSystemDataTool(int poId)
    {
        var po = await _dbContext.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(p => p.Id == poId)
            ?? throw new ArgumentException($"Purchase order {poId} not found.");

        var history = await _dbContext.Deliveries
            .Where(d => d.PurchaseOrder!.SupplierId == po.SupplierId && d.Status != DeliveryStatus.Scheduled)
            .ToListAsync();

        var totalDeliveries = history.Count;
        var lateDeliveries = history.Count(d => d.ActualArrivalDate > po.ExpectedDeliveryDate);
        var discrepancyDeliveries = history.Count(d => d.Status == DeliveryStatus.DiscrepancyReported);

        return new
        {
            PurchaseOrderId = po.Id,
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier?.Name ?? "Unknown",
            SupplierStatus = po.Supplier?.Status.ToString() ?? "Active",
            RequiredDate = po.OrderDate.AddDays(5), // Business logic for required date
            PromisedDate = po.ExpectedDeliveryDate ?? DateTime.UtcNow.AddDays(3),
            TotalPastDeliveries = totalDeliveries,
            LateDeliveriesCount = lateDeliveries,
            DiscrepancyDeliveriesCount = discrepancyDeliveries,
            OnTimeRate = totalDeliveries > 0 ? (double)(totalDeliveries - lateDeliveries) / totalDeliveries : 1.0,
            DiscrepancyRate = totalDeliveries > 0 ? (double)discrepancyDeliveries / totalDeliveries : 0.0,
            ItemSummary = po.Items.Select(i => i.Material?.Name).ToList()
        };
    }

    private async Task<DeliveryRiskAssessment> PerformRiskAnalysis(dynamic data)
    {
        // Perform factual calculations in backend
        int delayDays = (data.PromisedDate - data.RequiredDate).Days;
        double onTimeRatePercent = data.OnTimeRate * 100;

        string? apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            return PerformRuleBasedAssessment(data, delayDays, onTimeRatePercent);
        }

        try
        {
            var systemInstruction = "You are a 'Delivery Risk Agent'. Your objective is to interpret supplier delivery data. " +
                "Evaluate risk based on delay days (Promised Date - Required Date) and historical performance (On-Time Rate). " +
                "Output ONLY a valid JSON object matching this schema: " +
                "{\"riskLevel\": \"Low\"|\"Medium\"|\"High\", \"riskScore\": 0-100, \"reasons\": [\"string\"], \"warnings\": [\"string\"], \"recommendation\": \"string\"}.";

            var analysisPrompt = new
            {
                Supplier = data.SupplierName,
                DelayDays = delayDays,
                HistoricalOnTimeRate = $"{onTimeRatePercent:F1}%",
                HistoricalDiscrepancies = data.DiscrepancyDeliveriesCount,
                ItemCount = data.ItemSummary.Count
            };

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = $"{systemInstruction}\n\nData for Analysis:\n{JsonSerializer.Serialize(analysisPrompt)}" } } } },
                generationConfig = new { responseMimeType = "application/json" }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
            var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

                var assessment = JsonSerializer.Deserialize<DeliveryRiskAssessment>(text!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

                // Enrich with factual values from backend
                assessment.DelayDays = delayDays;
                assessment.OnTimeRate = (decimal)onTimeRatePercent;
                return assessment;
            }

            return PerformRuleBasedAssessment(data, delayDays, onTimeRatePercent);
        }
        catch
        {
            return PerformRuleBasedAssessment(data, delayDays, onTimeRatePercent);
        }
    }

    private DeliveryRiskAssessment PerformRuleBasedAssessment(dynamic data, int delayDays, double onTimeRate)
    {
        var assessment = new DeliveryRiskAssessment { DelayDays = delayDays, OnTimeRate = (decimal)onTimeRate };

        if (delayDays > 2 || onTimeRate < 70)
        {
            assessment.RiskLevel = "High";
            assessment.RiskScore = 85;
            assessment.Reasons.Add(delayDays > 0 ? $"Committed delivery is {delayDays} days late." : "Supplier has poor historical on-time performance.");
            assessment.Recommendation = "Flag for manual procurement review.";
        }
        else if (delayDays > 0 || onTimeRate < 90)
        {
            assessment.RiskLevel = "Medium";
            assessment.RiskScore = 50;
            assessment.Reasons.Add("Slight delay or minor historical issues detected.");
            assessment.Recommendation = "Monitor delivery closely.";
        }
        else
        {
            assessment.RiskLevel = "Low";
            assessment.RiskScore = 15;
            assessment.Reasons.Add("Supplier has strong history and satisfies required date.");
            assessment.Recommendation = "Proceed with standard receiving workflow.";
        }

        return assessment;
    }

    private object ValidateAssessment(DeliveryRiskAssessment assessment)
    {
        bool isValid = !string.IsNullOrEmpty(assessment.RiskLevel) && assessment.RiskScore >= 0 && assessment.RiskScore <= 100;
        return new { Valid = isValid, Timestamp = DateTime.UtcNow };
    }
}

public class DeliveryRiskAssessment
{
    public string RiskLevel { get; set; } = "Low";
    public int RiskScore { get; set; }
    public int DelayDays { get; set; }
    public decimal OnTimeRate { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;
}

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

    public async Task<DeliveryRiskAssessment> EvaluateDeliveryRiskAsync(int purchaseOrderId, int? userId = null)
    {
        var purchaseOrder = await _dbContext.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Project)
            .Include(po => po.Items)
                .ThenInclude(poi => poi.Material)
            .FirstOrDefaultAsync(po => po.Id == purchaseOrderId);

        if (purchaseOrder == null)
        {
            throw new ArgumentException("Purchase order not found.");
        }

        // 1. Gather historical metrics for this supplier
        var totalDeliveries = await _dbContext.Deliveries
            .CountAsync(d => d.PurchaseOrder!.SupplierId == purchaseOrder.SupplierId);

        var discrepancyDeliveries = await _dbContext.Deliveries
            .CountAsync(d => d.PurchaseOrder!.SupplierId == purchaseOrder.SupplierId && 
                             d.Status == DeliveryStatus.DiscrepancyReported);

        double historicalDiscrepancyRate = totalDeliveries > 0 
            ? (double)discrepancyDeliveries / totalDeliveries 
            : 0.0;

        // 2. Create the Agentic Workflow audit record
        var workflow = new AgentWorkflow
        {
            PurchaseOrderId = purchaseOrder.Id,
            InitiatedByUserId = userId,
            Objective = $"Evaluate delivery risk for Purchase Order #{purchaseOrder.Id} (Supplier: {purchaseOrder.Supplier?.Name})",
            Status = WorkflowStatus.Running,
            ApprovalStatus = AgentApprovalStatus.Pending,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AgentWorkflows.Add(workflow);
        await _dbContext.SaveChangesAsync();

        var step = new AgentWorkflowStep
        {
            AgentWorkflowId = workflow.Id,
            AgentRole = "Delivery Risk Agent",
            StepName = "Analyze Promised Dates vs Required Dates and Supplier History",
            StepOrder = 1,
            Status = WorkflowStepStatus.Running,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AgentWorkflowSteps.Add(step);
        await _dbContext.SaveChangesAsync();

        // 3. Prepare data for the evaluation
        var promisedDate = purchaseOrder.ExpectedDeliveryDate ?? DateTime.UtcNow.AddDays(3);
        var requiredDate = purchaseOrder.OrderDate.AddDays(5); // Simulate required date as order date + 5 days if not otherwise set
        var daysDifference = (promisedDate - requiredDate).Days;
        
        var promptData = new
        {
            PurchaseOrderId = purchaseOrder.Id,
            SupplierName = purchaseOrder.Supplier?.Name ?? "Unknown Supplier",
            SupplierStatus = purchaseOrder.Supplier?.Status.ToString() ?? "Active",
            PromisedDeliveryDate = promisedDate.ToString("yyyy-MM-dd"),
            RequiredDeliveryDate = requiredDate.ToString("yyyy-MM-dd"),
            DaysDifference = daysDifference,
            TotalPastDeliveries = totalDeliveries,
            DiscrepancyDeliveries = discrepancyDeliveries,
            DiscrepancyRate = historicalDiscrepancyRate,
            Items = purchaseOrder.Items.Select(i => new { MaterialName = i.Material?.Name, Quantity = i.OrderedQuantity })
        };

        DeliveryRiskAssessment assessment;
        string? apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            // Graceful Fallback: Rule-based evaluation
            assessment = PerformRuleBasedRiskAssessment(promptData);
            step.StructuredResultJson = JsonSerializer.Serialize(assessment);
            step.ValidationResultJson = JsonSerializer.Serialize(new { Valid = true, Source = "RuleBasedFallback" });
            step.Status = WorkflowStepStatus.Completed;
            step.CompletedAt = DateTime.UtcNow;
            
            workflow.Status = WorkflowStatus.Completed;
            workflow.FinalOutcome = $"Rule-based assessment completed. Risk: {assessment.RiskLevel}";
            workflow.CompletedAt = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync();
            return assessment;
        }

        try
        {
            // Call Gemini API
            var systemInstruction = "You are a 'Delivery Risk Agent' for BuildSupply LK, a construction materials procurement system. " +
                                     "Your task is to analyze if a supplier quotation/purchase order creates delivery risk. " +
                                     "You must evaluate: " +
                                     "1. Promised delivery date vs project required date. " +
                                     "2. Historical delivery performance (discrepancy rates). " +
                                     "You must return ONLY a structured JSON response matching the schema: " +
                                     "{\"riskLevel\": \"Low\"|\"Medium\"|\"High\", \"justification\": \"string explanation\", \"warnings\": [\"string warnings\"]}. " +
                                     "Do not include any markdown format tags like ```json or ```. Output raw JSON string.";

            var userPrompt = $"Analyze this delivery risk data:\n{JsonSerializer.Serialize(promptData, new JsonSerializerOptions { WriteIndented = true })}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"{systemInstruction}\n\nUser Input Data:\n{userPrompt}" }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var response = await _httpClient.PostAsync(
                url, 
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var textResult = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                // Parse the inner JSON returned by Gemini
                assessment = JsonSerializer.Deserialize<DeliveryRiskAssessment>(textResult!, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? PerformRuleBasedRiskAssessment(promptData);

                step.StructuredResultJson = JsonSerializer.Serialize(assessment);
                step.ValidationResultJson = JsonSerializer.Serialize(new { Valid = true, Source = "GeminiApi" });
                step.Status = WorkflowStepStatus.Completed;
            }
            else
            {
                throw new Exception($"Gemini API returned status code: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            // Fallback in case of API failure
            assessment = PerformRuleBasedRiskAssessment(promptData);
            step.StructuredResultJson = JsonSerializer.Serialize(assessment);
            step.ValidationResultJson = JsonSerializer.Serialize(new { Valid = true, Source = "ErrorFallback" });
            step.ErrorMessage = ex.Message;
            step.Status = WorkflowStepStatus.Completed;
        }

        step.CompletedAt = DateTime.UtcNow;
        workflow.Status = WorkflowStatus.Completed;
        workflow.FinalOutcome = $"Gemini assessment completed. Risk: {assessment.RiskLevel}";
        workflow.CompletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return assessment;
    }

    private DeliveryRiskAssessment PerformRuleBasedRiskAssessment(dynamic data)
    {
        string riskLevel = "Low";
        var warnings = new List<string>();
        var justification = new StringBuilder();

        justification.Append($"Analyzed delivery for supplier '{data.SupplierName}'. ");

        if (data.SupplierStatus == "Suspended")
        {
            riskLevel = "High";
            warnings.Add("Supplier status is currently Suspended.");
            justification.Append("Supplier is suspended, creating an automatic high delivery risk. ");
        }

        if (data.DaysDifference > 0)
        {
            riskLevel = "High";
            warnings.Add($"Promised date is {data.DaysDifference} days after the project required date.");
            justification.Append($"The supplier cannot satisfy the required date (Promised: {data.PromisedDeliveryDate}, Required: {data.RequiredDeliveryDate}). ");
        }
        else if (data.DaysDifference == 0)
        {
            riskLevel = "Medium";
            warnings.Add("Delivery is scheduled on the exact day required, leaving zero room for delay buffers.");
            justification.Append("Delivery is scheduled exactly on the required date. ");
        }

        if (data.DiscrepancyRate > 0.2)
        {
            if (riskLevel != "High") riskLevel = "Medium";
            warnings.Add($"Supplier has a high past discrepancy rate of {(data.DiscrepancyRate * 100):F1}% across {data.TotalPastDeliveries} deliveries.");
            justification.Append($"Supplier shows history of delivery issues: {data.DiscrepancyDeliveries} of {data.TotalPastDeliveries} past shipments were flagged with discrepancies. ");
        }

        if (warnings.Count == 0)
        {
            justification.Append("Supplier exhibits strong on-time history and promised date satisfies required date. Risk is determined to be Low.");
        }

        return new DeliveryRiskAssessment
        {
            RiskLevel = riskLevel,
            Justification = justification.ToString(),
            Warnings = warnings
        };
    }
}

public class DeliveryRiskAssessment
{
    public string RiskLevel { get; set; } = "Low";
    public string Justification { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

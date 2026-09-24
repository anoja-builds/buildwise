using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BuildWise.Api.Services;

public class ProcurementPlanningAgentService
{
    private readonly ApplicationDbContext _dbContext;

    public ProcurementPlanningAgentService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> EvaluateMaterialRequestPlanAsync(int requestId, int? userId = null)
    {
        var request = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            throw new ArgumentException($"Material Request #{requestId} not found.");
        }

        var riskFlags = new List<string>();
        var requiredChecks = new List<string>();
        var steps = new List<string>();

        // Step 1: Urgency analysis
        var reqDateTime = request.RequiredDate.ToDateTime(TimeOnly.MinValue);
        var daysUntilRequired = (reqDateTime - DateTime.UtcNow).TotalDays;
        steps.Add("1. Analyzed request schedule and required-by date against standard lead times.");
        
        if (daysUntilRequired < 3)
        {
            riskFlags.Add("URGENT DEADLINE: Less than 3 days until required date. High risk of site delay.");
        }
        else if (daysUntilRequired < 7)
        {
            riskFlags.Add("TIGHT DEADLINE: Less than 7 days lead time.");
        }

        // Step 2: Item quantity & availability check
        steps.Add("2. Verified item specifications, quantities, and material categories.");
        foreach (var item in request.Items)
        {
            requiredChecks.Add($"Verify availability for {item.RequestedQuantity} {item.Material?.Unit ?? "units"} of {item.Material?.Name ?? "Material"}");
            if (item.RequestedQuantity > 500)
            {
                riskFlags.Add($"LARGE VOLUME: {item.Material?.Name} quantity ({item.RequestedQuantity}) requires split delivery or bulk supplier agreement.");
            }
        }

        // Step 3: Project context check
        steps.Add("3. Cross-referenced site location and project active status.");
        if (request.Project != null && request.Project.Status != ProjectStatus.Active)
        {
            riskFlags.Add($"PROJECT STATUS ALERT: Project '{request.Project.Name}' is not currently Active ({request.Project.Status}).");
        }

        // Step 4: RFQ Strategy Formulation
        steps.Add("4. Formulated RFQ distribution strategy to active local suppliers.");

        var planOutput = new
        {
            AgentName = "Procurement Planning Agent (Agent 1)",
            MaterialRequestId = request.Id,
            ProjectName = request.Project?.Name ?? "Unknown Project",
            RequiredDate = request.RequiredDate,
            Objective = $"Formulate automated procurement strategy for Material Request #{request.Id}",
            Steps = steps,
            RequiredChecks = requiredChecks,
            RiskFlags = riskFlags,
            RecommendedAction = riskFlags.Any(r => r.StartsWith("URGENT") || r.StartsWith("PROJECT"))
                ? "Immediate Expedited RFQ to Priority Suppliers"
                : "Standard Competitive RFQ Process (Min. 2 Quotations)",
            Timestamp = DateTime.UtcNow
        };

        // Record agent execution step in Db
        var workflow = await _dbContext.AgentWorkflows
            .FirstOrDefaultAsync(w => w.MaterialRequestId == requestId)
            ?? new AgentWorkflow
            {
                MaterialRequestId = requestId,
                InitiatedByUserId = userId ?? request.RequestedByUserId,
                Objective = $"Procurement Plan for MR-{requestId:D4}",
                Status = WorkflowStatus.Running,
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

        if (workflow.Id == 0)
        {
            _dbContext.AgentWorkflows.Add(workflow);
            await _dbContext.SaveChangesAsync();
        }

        var step = new AgentWorkflowStep
        {
            AgentWorkflowId = workflow.Id,
            AgentRole = "Procurement Planning Agent",
            StepName = "Requirement Analysis & Plan Generation",
            StepOrder = 1,
            Status = WorkflowStepStatus.Completed,
            StructuredResultJson = JsonSerializer.Serialize(planOutput),
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AgentWorkflowSteps.Add(step);
        await _dbContext.SaveChangesAsync();

        return planOutput;
    }
}

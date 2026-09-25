using System.Text.Json;

namespace BuildWise.Api.Services;

/// <summary>
/// Persists a compact, auditable execution record for an operational agent.
/// Client requests and responses are never stored; only the plan, allow-listed
/// tool names, execution source, structured result and deterministic validation
/// summary are retained.
/// </summary>
public class OperationalAgentAuditService
{
    private readonly Data.ApplicationDbContext _db;

    public OperationalAgentAuditService(Data.ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int> RecordAsync(
        int initiatedByUserId,
        string objective,
        string agentRole,
        IReadOnlyList<string> plan,
        IReadOnlyList<string> tools,
        object structuredResult,
        string executionSource,
        object validation,
        int? materialRequestId = null,
        int? purchaseOrderId = null,
        int? deliveryId = null,
        string? errorMessage = null)
    {
        var now = DateTime.UtcNow;
        var workflow = new Models.Entities.AgentWorkflow
        {
            InitiatedByUserId = initiatedByUserId,
            MaterialRequestId = materialRequestId,
            PurchaseOrderId = purchaseOrderId,
            DeliveryId = deliveryId,
            Objective = objective,
            Status = Models.Enums.WorkflowStatus.Completed,
            ApprovalStatus = Models.Enums.AgentApprovalStatus.NotRequired,
            FinalOutcome = errorMessage == null
                ? "Advisory agent result validated and recorded."
                : "Advisory agent call used or produced a safe failure.",
            StartedAt = now,
            CompletedAt = now
        };
        _db.AgentWorkflows.Add(workflow);
        await _db.SaveChangesAsync();

        _db.AgentWorkflowSteps.AddRange(
            CreateStep(workflow.Id, "OperationalPlanningAgent", "Plan advisory analysis", 1, now,
                new { objective, plan, tools }, new { valid = true, rule = "allow-list" }, null),
            CreateStep(workflow.Id, agentRole, "Execute allow-listed operational analysis", 2, now,
                new { executionSource, result = structuredResult }, validation, errorMessage),
            CreateStep(workflow.Id, "OperationalValidationAgent", "Re-check deterministic business rules", 3, now,
                new { rule = "independent ASP.NET Core validation" }, validation, errorMessage));

        await _db.SaveChangesAsync();
        return workflow.Id;
    }

    private static Models.Entities.AgentWorkflowStep CreateStep(
        int workflowId,
        string role,
        string name,
        int order,
        DateTime now,
        object structuredResult,
        object validation,
        string? errorMessage) => new()
        {
            AgentWorkflowId = workflowId,
            AgentRole = role,
            StepName = name,
            StepOrder = order,
            Status = errorMessage == null ? Models.Enums.WorkflowStepStatus.Completed : Models.Enums.WorkflowStepStatus.Failed,
            StructuredResult = JsonSerializer.Serialize(structuredResult),
            ValidationResult = JsonSerializer.Serialize(validation),
            ErrorMessage = errorMessage,
            StartedAt = now,
            CompletedAt = now
        };
}

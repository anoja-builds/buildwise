using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class AgentWorkflowStep
{
    public int Id { get; set; }

    public int AgentWorkflowId { get; set; }
    public AgentWorkflow AgentWorkflow { get; set; } = null!;

    public string AgentRole { get; set; } = string.Empty;

    public string StepName { get; set; } = string.Empty;

    public int StepOrder { get; set; }

    public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;

    /// <summary>
    /// Stored as JSON string (mirrors ERD jsonb column)
    /// </summary>
    public string? StructuredResult { get; set; }

    /// <summary>
    /// Stored as JSON string (mirrors ERD jsonb column)
    /// </summary>
    public string? ValidationResult { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}

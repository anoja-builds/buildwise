using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class AgentWorkflowStep : BaseEntity
{
    public int AgentWorkflowId { get; set; }

    public AgentWorkflow? AgentWorkflow { get; set; }

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

    /// <summary>C3 alias for <see cref="StructuredResult"/>.</summary>
    public string? StructuredResultJson
    {
        get => StructuredResult;
        set => StructuredResult = value;
    }

    /// <summary>C3 alias for <see cref="ValidationResult"/>.</summary>
    public string? ValidationResultJson
    {
        get => ValidationResult;
        set => ValidationResult = value;
    }

    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}

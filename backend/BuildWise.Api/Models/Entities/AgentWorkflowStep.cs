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

    public string? StructuredResultJson { get; set; }

    public string? ValidationResultJson { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}

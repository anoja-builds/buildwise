using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class AgentApproval
{
    public int Id { get; set; }

    public int AgentWorkflowId { get; set; }
    public AgentWorkflow AgentWorkflow { get; set; } = null!;

    public int ReviewedByUserId { get; set; }

    public AgentApprovalStatus Decision { get; set; }

    public string? Comment { get; set; }

    public DateTime DecisionDate { get; set; } = DateTime.UtcNow;
}

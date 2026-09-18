using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class AgentWorkflow : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest MaterialRequest { get; set; } = null!;

    public int InitiatedByUserId { get; set; }

    public string Objective { get; set; } = string.Empty;

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Pending;

    public AgentApprovalStatus ApprovalStatus { get; set; } = AgentApprovalStatus.Pending;

    public string? FinalOutcome { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public ICollection<AgentWorkflowStep> Steps { get; set; } = new List<AgentWorkflowStep>();

    public ICollection<AgentApproval> Approvals { get; set; } = new List<AgentApproval>();
}

namespace BuildWise.Api.Models.Enums;

public enum WorkflowStatus
{
    Pending,
    Running,
    RevisionRequired,
    AwaitingApproval,
    Completed,
    Failed,
    Cancelled
}

using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Approval : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest MaterialRequest { get; set; } = null!;

    public int ApprovedByUserId { get; set; }
    public User ApprovedByUser { get; set; } = null!;

    public ApprovalDecision Decision { get; set; }

    public string? Comments { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}

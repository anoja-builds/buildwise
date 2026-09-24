using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Approval : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest? MaterialRequest { get; set; }

    public int ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    public ApprovalDecision Decision { get; set; }

    public string? Comment { get; set; }

    public DateTime DecisionDate { get; set; } = DateTime.UtcNow;
}

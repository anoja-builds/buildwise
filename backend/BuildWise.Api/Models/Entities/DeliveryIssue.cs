using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class DeliveryIssue : BaseEntity
{
    public int DeliveryId { get; set; }
    public Delivery? Delivery { get; set; }

    public int? DeliveryItemId { get; set; }
    public DeliveryItem? DeliveryItem { get; set; }

    public DeliveryIssueType IssueType { get; set; }
    public string Description { get; set; } = string.Empty;
    public DeliveryIssueSeverity Severity { get; set; }

    public string Status { get; set; } = "Open"; // Open, Pending, Resolved, Closed
    public string? Resolution { get; set; }

    public int? ReportedByUserId { get; set; }
    public User? ReportedByUser { get; set; }

    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

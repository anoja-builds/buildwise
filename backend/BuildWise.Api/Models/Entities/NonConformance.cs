using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class NonConformance : BaseEntity
{
    public int InspectionItemId { get; set; }

    public InspectionItem? InspectionItem { get; set; }

    public string IssueDescription { get; set; } = string.Empty;

    public NonConformanceSeverity Severity { get; set; }

    public string? CorrectiveAction { get; set; }

    public NonConformanceStatus Status { get; set; } = NonConformanceStatus.Open;

    public DateTime? ResolvedAt { get; set; }
}

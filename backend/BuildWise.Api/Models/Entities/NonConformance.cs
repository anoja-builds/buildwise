using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class NonConformance
{
    public int Id { get; set; }
    public string NcrNumber { get; set; } = string.Empty;
    public int InspectionItemId { get; set; }
    public InspectionItem? InspectionItem { get; set; }
    public int DeliveryId { get; set; }
    public int MaterialId { get; set; }
    public int? SupplierId { get; set; }
    public decimal QuantityAffected { get; set; }
    public NonConformanceSeverity Severity { get; set; } = NonConformanceSeverity.Medium;
    public NonConformanceStatus Status { get; set; } = NonConformanceStatus.Open;
    public string IssueDescription { get; set; } = string.Empty;
    public string CorrectiveActionPlan { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? ReviewNotes { get; set; }
    public int? ResponsibleUserId { get; set; }
    public User? ResponsibleUser { get; set; }
    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

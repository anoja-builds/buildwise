using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Inspection
{
    public int Id { get; set; }
    public int DeliveryId { get; set; }
    public Delivery? Delivery { get; set; }
    public int InspectorUserId { get; set; }
    public InspectionStatus Status { get; set; } = InspectionStatus.Pending;
    public InspectionDecision OverallDecision { get; set; } = InspectionDecision.Accepted;
    public DateTime InspectedAt { get; set; } = DateTime.UtcNow;
    public string? InspectionCriteria { get; set; }
    public string? ObservedResult { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<InspectionItem> Items { get; set; } = new();
    public List<InspectionEvidence> Evidence { get; set; } = new();
}

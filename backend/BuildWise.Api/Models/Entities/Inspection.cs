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

    // The five-point quality checklist (quantity, visual condition, moisture,
    // packaging, defects). These are structured pass/fail observations rather
    // than one free-text criteria blob, so the UI can render each point
    // individually and so a missing check is visible instead of hidden inside a
    // sentence.
    //
    // Nullable rather than non-nullable deliberately: a column added to an
    // existing database must be able to hold NULL for inspections recorded
    // before the checklist existed. Completeness is enforced at the service
    // boundary on completion, not by the column type, so historical rows stay
    // readable and are not back-filled with a fabricated "pass".
    public bool? QuantityCheck { get; set; }
    public bool? VisualConditionCheck { get; set; }
    public bool? MoistureCheck { get; set; }
    public bool? PackagingCheck { get; set; }
    public bool? DefectsCheck { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<InspectionItem> Items { get; set; } = new();
    public List<InspectionEvidence> Evidence { get; set; } = new();
}

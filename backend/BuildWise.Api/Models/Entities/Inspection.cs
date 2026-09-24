using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Inspection : BaseEntity
{
    public int DeliveryId { get; set; }

    public Delivery? Delivery { get; set; }

    public int InspectorUserId { get; set; }

    public User? Inspector { get; set; }

    public DateTime InspectionDate { get; set; }

    public InspectionStatus Status { get; set; } = InspectionStatus.Pending;

    public InspectionDecision? OverallDecision { get; set; }

    public string? Notes { get; set; }

    public ICollection<InspectionItem> Items { get; set; } = new List<InspectionItem>();
}

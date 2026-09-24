using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class InspectionItem : BaseEntity
{
    public int InspectionId { get; set; }

    public Inspection? Inspection { get; set; }

    public int DeliveryItemId { get; set; }

    public DeliveryItem? DeliveryItem { get; set; }

    public string? Condition { get; set; }

    public decimal AcceptedQuantity { get; set; } = 0;

    public decimal RejectedQuantity { get; set; } = 0;

    public string? Remarks { get; set; }

    public ICollection<NonConformance> NonConformances { get; set; } = new List<NonConformance>();
}

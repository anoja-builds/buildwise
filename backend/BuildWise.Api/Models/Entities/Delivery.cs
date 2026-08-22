using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Delivery : BaseEntity
{
    public int PurchaseOrderId { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }

    public DateTime? ActualArrivalDate { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public int? ReceivedByUserId { get; set; }

    public User? ReceivedByUser { get; set; }

    public string? DeliveryReference { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Scheduled;

    public string? Notes { get; set; }

    public string? PhotographicEvidenceUrl { get; set; }

    public ICollection<DeliveryItem> Items { get; set; } = new List<DeliveryItem>();

    public ICollection<DeliveryIssue> Issues { get; set; } = new List<DeliveryIssue>();

    public ICollection<DeliveryEvidence> Evidence { get; set; } = new List<DeliveryEvidence>();
}

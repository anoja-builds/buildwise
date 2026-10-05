using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Delivery
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public int ReceivedByUserId { get; set; }
    public string DeliveryReference { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Arrived;
    public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
    public List<DeliveryItem> Items { get; set; } = new();
}

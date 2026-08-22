using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class PurchaseOrder : BaseEntity
{
    public int SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public int ProjectId { get; set; }

    public Project? Project { get; set; }

    public DateTime OrderDate { get; set; }

    public DateTime? ExpectedDeliveryDate { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Created;

    public decimal TotalAmount { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();

    public ICollection<Delivery> Deliveries { get; set; } = new List<Delivery>();
}

using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class PurchaseOrder : BaseEntity
{
    public int QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    public DateOnly OrderDate { get; set; }

    public DateOnly? ExpectedDeliveryDate { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Created;

    public decimal TotalAmount { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

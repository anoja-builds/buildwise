namespace BuildWise.Api.Models.Entities;

public class PurchaseOrderItem
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public int QuotationItemId { get; set; }
    public QuotationItem QuotationItem { get; set; } = null!;

    public decimal OrderedQuantity { get; set; }

    public decimal UnitPrice { get; set; }
}

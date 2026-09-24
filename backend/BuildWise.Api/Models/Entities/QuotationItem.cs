namespace BuildWise.Api.Models.Entities;

public class QuotationItem
{
    public int Id { get; set; }

    public int QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    public int MaterialRequestItemId { get; set; }
    public MaterialRequestItem MaterialRequestItem { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public PurchaseOrderItem? PurchaseOrderItem { get; set; }
}

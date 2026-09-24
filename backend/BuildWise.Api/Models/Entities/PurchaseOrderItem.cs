using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class PurchaseOrderItem : BaseEntity
{
    public int PurchaseOrderId { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }

    public int? QuotationItemId { get; set; }
    public QuotationItem? QuotationItem { get; set; }

    public int? MaterialId { get; set; }

    public Material? Material { get; set; }

    public decimal OrderedQuantity { get; set; }

    public decimal UnitPrice { get; set; }
}

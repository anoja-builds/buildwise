using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Quotation : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest MaterialRequest { get; set; } = null!;

    public int? RfqId { get; set; }
    public Rfq? Rfq { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateOnly QuotationDate { get; set; }

    public DateOnly ValidUntil { get; set; }
    public DateOnly? PromisedDeliveryDate { get; set; }

    public QuotationStatus Status { get; set; } = QuotationStatus.Submitted;

    public decimal TotalAmount { get; set; }
    public decimal TransportCharge { get; set; }
    public string? PaymentTerms { get; set; }

    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();

    public PurchaseOrder? PurchaseOrder { get; set; }
}

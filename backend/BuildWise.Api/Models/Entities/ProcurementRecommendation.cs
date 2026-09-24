using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class ProcurementRecommendation : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest? MaterialRequest { get; set; }

    public int? RfqId { get; set; }
    public Rfq? Rfq { get; set; }

    public int RecommendedSupplierId { get; set; }
    public Supplier? RecommendedSupplier { get; set; }

    public int? RecommendedQuotationId { get; set; }
    public Quotation? RecommendedQuotation { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string Justification { get; set; } = string.Empty;

    public string RiskFlagsJson { get; set; } = "[]";

    public RecommendationStatus Status { get; set; } = RecommendationStatus.AwaitingApproval;

    public int? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    public string? DecisionComment { get; set; }

    public DateTime? DecisionDate { get; set; }

    public int? GeneratedPurchaseOrderId { get; set; }
    public PurchaseOrder? GeneratedPurchaseOrder { get; set; }
}

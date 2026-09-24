using System.ComponentModel.DataAnnotations;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Dtos;

public class CreateSupplierDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string ContactPerson { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public SupplierStatus Status { get; set; } = SupplierStatus.Active;
}

public class CreateRfqDto
{
    [Required]
    public int MaterialRequestId { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime? Deadline { get; set; }

    public List<int> SupplierIds { get; set; } = new();
}

public class CreateQuotationItemDto
{
    public int MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TransportCharge { get; set; }
}

public class CreateQuotationDto
{
    public int MaterialRequestId { get; set; }

    public int? RfqId { get; set; }

    [Required]
    public int SupplierId { get; set; }

    [Required]
    public string QuotationNumber { get; set; } = string.Empty;

    public DateTime QuotationDate { get; set; } = DateTime.UtcNow;

    public DateTime ValidityDate { get; set; } = DateTime.UtcNow.AddDays(14);

    public DateTime PromisedDeliveryDate { get; set; } = DateTime.UtcNow.AddDays(5);

    public decimal TransportCharge { get; set; }

    public decimal TaxAmount { get; set; }

    public string PaymentTerms { get; set; } = "Net 30";

    public string? SupplierNotes { get; set; }

    public List<CreateQuotationItemDto> Items { get; set; } = new();
}

public class ProcurementApprovalDto
{
    [Required]
    public int UserId { get; set; }

    public RecommendationStatus Decision { get; set; } = RecommendationStatus.Approved;

    public string? Comment { get; set; }
}

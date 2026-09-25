using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Rfq : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest MaterialRequest { get; set; } = null!;
    public int IssuedByUserId { get; set; }
    public DateOnly RequiredResponseDate { get; set; }
    public string? Notes { get; set; }
    public RfqStatus Status { get; set; } = RfqStatus.Issued;
    public ICollection<RfqSupplier> Suppliers { get; set; } = new List<RfqSupplier>();
}

public class RfqSupplier : BaseEntity
{
    public int RfqId { get; set; }
    public Rfq Rfq { get; set; } = null!;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public RfqSupplierStatus Status { get; set; } = RfqSupplierStatus.Invited;
    public DateTime? RespondedAt { get; set; }
}

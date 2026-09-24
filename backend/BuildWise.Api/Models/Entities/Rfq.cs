using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Rfq : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest? MaterialRequest { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public RfqStatus Status { get; set; } = RfqStatus.Draft;

    public DateTime? Deadline { get; set; }

    public ICollection<RfqSupplier> Suppliers { get; set; } = new List<RfqSupplier>();

    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
}

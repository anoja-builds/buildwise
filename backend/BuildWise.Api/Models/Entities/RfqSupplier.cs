using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class RfqSupplier : BaseEntity
{
    public int RfqId { get; set; }
    public Rfq? Rfq { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string Status { get; set; } = "Invited"; // Invited, Responded, Declined

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

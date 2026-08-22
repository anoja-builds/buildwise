using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class DeliveryEvidence : BaseEntity
{
    public int DeliveryId { get; set; }
    public Delivery? Delivery { get; set; }

    public int? DeliveryItemId { get; set; }
    public DeliveryItem? DeliveryItem { get; set; }

    public string FileUrl { get; set; } = string.Empty;
    public string? FileType { get; set; } // e.g. "Image", "PDF"

    public int? UploadedByUserId { get; set; }
    public User? UploadedByUser { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

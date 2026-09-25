using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class InspectionEvidence : BaseEntity
{
    public int InspectionId { get; set; }
    public Inspection Inspection { get; set; } = null!;
    public int? InspectionItemId { get; set; }
    public InspectionItem? InspectionItem { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long FileSizeBytes { get; set; }
    public int UploadedByUserId { get; set; }
    public User? UploadedByUser { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

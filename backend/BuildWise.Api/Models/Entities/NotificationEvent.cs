using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class NotificationEvent : BaseEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? MaterialRequestId { get; set; }
    public int? DeliveryId { get; set; }
    public int? InspectionId { get; set; }
    public int? NonConformanceId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}

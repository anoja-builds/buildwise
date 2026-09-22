using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class DeliverySchedule : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public DateTime ScheduledDate { get; set; }
    public string? ScheduledTimeSlot { get; set; } // e.g. "Morning", "14:00 - 16:00"

    public string? Status { get; set; } // e.g. "Confirmed", "Rescheduled"
    public string? Notes { get; set; }
}

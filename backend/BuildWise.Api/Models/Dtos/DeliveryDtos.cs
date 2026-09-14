using System.Collections.Generic;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Dtos;

public class CreateDeliveryDto
{
    public int PurchaseOrderId { get; set; }
    public string DeliveryReference { get; set; } = string.Empty;
}

public class ReceiveDeliveryDto
{
    public int ReceivedByUserId { get; set; }
    public string? Notes { get; set; }
    public List<ReceiveDeliveryItemDto> Items { get; set; } = new();
}

public class ReceiveDeliveryItemDto
{
    public int PurchaseOrderItemId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class EvidenceDto
{
    public string ImageUrl { get; set; } = string.Empty;
}

public class ScheduleDeliveryDto
{
    public int PurchaseOrderId { get; set; }
    public DateTime ScheduledDate { get; set; }
    public string? TimeSlot { get; set; }
    public string? Notes { get; set; }
}

public class ReportIssueDto
{
    public int DeliveryId { get; set; }
    public int? DeliveryItemId { get; set; }
    public DeliveryIssueType IssueType { get; set; }
    public string Description { get; set; } = string.Empty;
    public DeliveryIssueSeverity Severity { get; set; }
    public int ReportedByUserId { get; set; }
}

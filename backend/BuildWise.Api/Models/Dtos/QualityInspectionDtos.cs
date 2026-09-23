using System.ComponentModel.DataAnnotations;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Dtos;

public class StartInspectionDto
{
    [Range(1, int.MaxValue)]
    public int DeliveryId { get; set; }

    public string? Notes { get; set; }
}

public class CompleteInspectionDto
{
    // Nullable so an omitted decision cannot silently become Accepted (enum value 0).
    [Required]
    [EnumDataType(typeof(InspectionDecision))]
    public InspectionDecision? OverallDecision { get; set; }

    public string? Notes { get; set; }

    [Required, MinLength(1)]
    public List<CompleteInspectionItemDto> Items { get; set; } = new();
}

public class CompleteInspectionItemDto
{
    [Range(1, int.MaxValue)]
    public int DeliveryItemId { get; set; }

    [MaxLength(100)]
    public string? Condition { get; set; }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal AcceptedQuantity { get; set; }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal RejectedQuantity { get; set; }

    public string? Remarks { get; set; }
}

public class PendingInspectionDeliveryDto
{
    public int DeliveryId { get; set; }
    public string? DeliveryReference { get; set; }
    public DeliveryStatus Status { get; set; }
    public List<InspectionDeliveryItemDto> Items { get; set; } = new();
}

public class InspectionDeliveryItemDto
{
    public int DeliveryItemId { get; set; }
    public int PurchaseOrderItemId { get; set; }
    public decimal ReceivedQuantity { get; set; }
}

public class QualityInspectionResponseDto
{
    public int Id { get; set; }
    public int DeliveryId { get; set; }
    public int InspectorUserId { get; set; }
    public DateTime InspectionDate { get; set; }
    public InspectionStatus Status { get; set; }
    public InspectionDecision? OverallDecision { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<QualityInspectionItemResponseDto> Items { get; set; } = new();
}

public class QualityInspectionItemResponseDto
{
    public int Id { get; set; }
    public int DeliveryItemId { get; set; }
    public string? Condition { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public string? Remarks { get; set; }
}

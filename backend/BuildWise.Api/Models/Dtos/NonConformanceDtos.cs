using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Dtos;

public class CreateNonConformanceDto
{
    [Range(1, int.MaxValue)]
    public int InspectionItemId { get; set; }

    [Required]
    public string IssueDescription { get; set; } = string.Empty;

    [JsonRequired]
    [EnumDataType(typeof(NonConformanceSeverity))]
    public NonConformanceSeverity Severity { get; set; }

    public string? CorrectiveAction { get; set; }
}

public class UpdateCorrectiveActionDto
{
    [Required]
    public string CorrectiveAction { get; set; } = string.Empty;
}

public class NonConformanceResponseDto
{
    public int Id { get; set; }
    public int InspectionItemId { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public NonConformanceSeverity Severity { get; set; }
    public string? CorrectiveAction { get; set; }
    public NonConformanceStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int InspectionId { get; set; }
    public int DeliveryItemId { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public string? Condition { get; set; }
    public string? Remarks { get; set; }
}

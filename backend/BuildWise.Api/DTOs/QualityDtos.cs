using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.DTOs;

public record InspectionEvidenceDto(
    string FileName,
    string FileUrl,
    string ContentType = "image/jpeg",
    long FileSizeBytes = 0,
    int? InspectionItemId = null
);

public class CompleteInspectionDto
{
    public int DeliveryId { get; set; }
    public string? InspectionCriteria { get; set; }
    public string? ObservedResult { get; set; }
    public string? Notes { get; set; }
    public List<InspectionEvidenceDto> Evidence { get; set; } = new();
    public List<InspectionItemInputDto> Items { get; set; } = new();
}

public record InspectionItemInputDto(
    int MaterialId,
    decimal InspectedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    string? RejectionReason
);

public record InspectionSummaryDto(
    int Id,
    int DeliveryId,
    int InspectorUserId,
    string Status,
    string OverallDecision,
    DateTime InspectedAt,
    string? InspectionCriteria,
    string? ObservedResult,
    string? Notes,
    int ItemCount,
    int EvidenceCount
);

public record NotificationDto(
    int Id,
    string Type,
    string Title,
    string Body,
    int? MaterialRequestId,
    int? DeliveryId,
    int? InspectionId,
    int? NonConformanceId,
    bool IsRead,
    DateTime CreatedAt
);

public record NcrReviewRequest(
    NonConformanceStatus Status,
    string? ReviewNotes,
    int? ResponsibleUserId,
    string? Resolution
);

public record NcrDto(
    int Id,
    string NcrNumber,
    int InspectionItemId,
    int DeliveryId,
    int MaterialId,
    int? SupplierId,
    decimal QuantityAffected,
    string Severity,
    string Status,
    string IssueDescription,
    string CorrectiveActionPlan,
    string? Resolution,
    string? ReviewNotes,
    int? ResponsibleUserId,
    int? ReviewedByUserId,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    DateTime? ResolvedAt,
    DateTime? ClosedAt
);

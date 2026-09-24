using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildWise.Api.Models.Dtos;

public record QualityItemEvidence(int InspectionItemId, int DeliveryItemId, int? MaterialId,
    string? MaterialName, string? Unit, decimal ReceivedQuantity, decimal DamagedQuantity,
    decimal AcceptedQuantity, decimal RejectedQuantity, decimal RejectionRate,
    decimal InspectionCoverage, string? Condition, string? Remarks);
public record QualityHistoryEvidence(int InspectionId, int DeliveryId, DateTime InspectionDate,
    string OverallDecision, int ItemCount, int RejectedItemCount);
public record QualityNcrEvidence(int Id, int InspectionItemId, string Severity, string Status,
    string IssueDescription, string? CorrectiveAction);
public record QualityDiscrepancyEvidence(int DeliveryId, string Status);
public record QualityIssueEvidence(int Id, int DeliveryId, string IssueType, string Severity, string Description);
public record QualityRiskEvidence(int InspectionId, int DeliveryId, int SupplierId, string SupplierName,
    string SupplierStatus, DateTime InspectionDate, string OverallDecision, DateTime CollectedAt,
    List<QualityItemEvidence> Items, List<QualityHistoryEvidence> History, string HistoryNote,
    int PriorNcrCount, List<QualityNcrEvidence> PriorNcrs, bool NcrsTruncated,
    List<QualityDiscrepancyEvidence> Discrepancies, bool DiscrepanciesTruncated,
    List<QualityIssueEvidence> DeliveryIssues, bool DeliveryIssuesTruncated, List<string> EvidenceReferences);

public class QualityRiskFlag
{
    public required string Flag { get; init; }
    public required List<string> EvidenceReferences { get; init; }
}
public class QualityRiskItemRecommendation
{
    public required int InspectionItemId { get; init; }
    public required bool NcrRecommended { get; init; }
    public required string? SuggestedSeverity { get; init; }
    public required string? SuggestedIssueDescription { get; init; }
    public required string? SuggestedCorrectiveAction { get; init; }
    public required string Rationale { get; init; }
    public required List<string> EvidenceReferences { get; init; }
}
public class QualityRiskRecommendation
{
    public required int InspectionId { get; init; }
    public required string RiskLevel { get; init; }
    public required List<QualityRiskFlag> RiskFlags { get; init; }
    public required string EvidenceSummary { get; init; }
    public required bool NcrRecommended { get; init; }
    public required List<QualityRiskItemRecommendation> ItemRecommendations { get; init; }
    public required string RationaleSummary { get; init; }
}
public class QualityAgentTrace
{
    public required int Iteration { get; init; }
    public required string Action { get; init; }
    public required Dictionary<string, int> Arguments { get; init; }
    public required bool Success { get; init; }
    public required double DurationMs { get; init; }
}
public class QualityAgentResult
{
    public required bool Success { get; init; }
    public required QualityRiskRecommendation? Recommendation { get; init; }
    public required List<QualityAgentTrace> Trace { get; init; }
    public required int IterationCount { get; init; }
    public required string ModelIdentifier { get; init; }
    public required string? ErrorCode { get; init; }
}
public record QualityRiskStepResponse(int StepOrder, string StepName, string Status,
    DateTime? StartedAt, DateTime? CompletedAt, JsonElement? StructuredResult,
    JsonElement? ValidationResult, string? Error);
public record QualityRiskWorkflowResponse(int WorkflowId, string Objective, int InspectionId,
    int? DeliveryId, string Status, string ApprovalStatus, string? FinalOutcome,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? StartedAt, DateTime? CompletedAt,
    List<QualityRiskStepResponse> Steps);

public static class QualityAgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 32
    };
}

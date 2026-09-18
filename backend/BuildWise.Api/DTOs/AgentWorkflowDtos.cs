namespace BuildWise.Api.DTOs;

public record StartProcurementWorkflowRequest(
    int InitiatedByUserId = 1,
    string? Objective = null
);

public record StartProcurementWorkflowResponse(
    int WorkflowId,
    string Status,
    string Message
);

public record RankedAlternativeDto(
    int QuotationId,
    int SupplierId,
    string SupplierName,
    int Rank,
    decimal TotalAmount,
    string Reason
);

public record AgentRecommendationDto(
    int? RecommendedQuotationId,
    int? RecommendedSupplierId,
    string? RecommendedSupplierName,
    string Rationale,
    List<RankedAlternativeDto> RankedAlternatives,
    List<string> Warnings
);

public record ProcurementValidationResultDto(
    bool IsValid,
    List<string> Errors
);

public record WorkflowDecisionDto(
    string Decision, // "Approve" | "Reject" | "RevisionRequested"
    string? Comment,
    int ReviewedByUserId = 1
);

public record AgentWorkflowStepDto(
    int Id,
    string AgentRole,
    string StepName,
    int StepOrder,
    string Status,
    string? StructuredResult,
    string? ValidationResult,
    string? ErrorMessage,
    DateTime? StartedAt,
    DateTime? CompletedAt
);

public record ProcurementWorkflowDetailsDto(
    int Id,
    int MaterialRequestId,
    string Objective,
    string Status,
    string ApprovalStatus,
    string? FinalOutcome,
    AgentRecommendationDto? Recommendation,
    ProcurementValidationResultDto? Validation,
    List<AgentWorkflowStepDto> Steps,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

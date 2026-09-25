namespace BuildWise.Api.DTOs;

public record ProcurementPlanningInput(
    int MaterialRequestId,
    string? Objective = null
);

public record ProcurementPlanningItemFact(
    int MaterialRequestItemId,
    int MaterialId,
    string MaterialName,
    string Unit,
    decimal RequestedQuantity
);

public record ProcurementPlanningInputFacts(
    int MaterialRequestId,
    int ProjectId,
    string ProjectName,
    string ProjectStatus,
    DateOnly RequestDate,
    DateOnly RequiredDate,
    string Priority,
    string RequestStatus,
    List<ProcurementPlanningItemFact> Items,
    int AvailableQuotationCount
);

public record ProcurementPlanningStep(
    int StepOrder,
    string Name,
    string Action,
    string DelegatedAgentRole,
    bool RequiresHumanApproval = false
);

public record ProcurementPlanningToolResult(
    string ToolName,
    bool Succeeded,
    string Summary
);

public record ProcurementPlanningOutput(
    string AgentRole,
    string Objective,
    List<ProcurementPlanningStep> Steps,
    List<string> RequiredChecks,
    List<string> RiskFlags,
    ProcurementPlanningInputFacts Input,
    List<string> AllowedTools,
    List<string> ProhibitedCapabilities,
    List<ProcurementPlanningToolResult> ToolResults
);

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
    List<string> Warnings,
    List<string>? Justification = null,
    List<string>? RiskFlags = null,
    List<RankedAlternativeDto>? Ranking = null
);

public record ProcurementValidationResultDto(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings
);

public record WorkflowDecisionDto(
    string Decision, // "Approve" | "Reject" | "RevisionRequested"
    string? Comment
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

public record AgentWorkflowSummaryDto(
    int Id,
    string Objective,
    string Status,
    string ApprovalStatus,
    string? FinalOutcome,
    int? MaterialRequestId,
    int? PurchaseOrderId,
    int? DeliveryId,
    int InitiatedByUserId,
    int StepCount,
    int FailedStepCount,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime CreatedAt);

public record AgentWorkflowDetailsDto(
    AgentWorkflowSummaryDto Workflow,
    List<AgentWorkflowStepDto> Steps,
    List<AgentWorkflowApprovalDto> Approvals);

public record AgentWorkflowApprovalDto(
    int Id,
    int ReviewedByUserId,
    string Decision,
    string? Comment,
    DateTime DecisionDate);

public record ProcurementWorkflowDetailsDto(
    int Id,
    int MaterialRequestId,
    string Objective,
    string Status,
    string ApprovalStatus,
    string? FinalOutcome,
    int? PurchaseOrderId,
    AgentRecommendationDto? Recommendation,
    ProcurementValidationResultDto? Validation,
    List<AgentWorkflowStepDto> Steps,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ProcurementPlanningOutput? Planning = null
);

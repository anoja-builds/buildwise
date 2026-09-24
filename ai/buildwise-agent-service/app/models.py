from datetime import datetime
from decimal import Decimal
from typing import Annotated, Literal
from pydantic import AfterValidator, BaseModel, ConfigDict, Field, model_validator


def require_nonblank(value: str) -> str:
    if not value.strip():
        raise ValueError('Text must not be blank or whitespace-only')
    return value

Level = Literal['Low', 'Medium', 'High', 'Critical']
Decision = Literal['Accepted', 'PartiallyAccepted', 'Rejected']
Text = Annotated[str, Field(min_length=1, max_length=2000)]
Reference = Annotated[str, Field(min_length=1, max_length=100)]
RecommendationText = Annotated[Text, AfterValidator(require_nonblank)]
Quantity = Annotated[Decimal, Field(ge=0, le=Decimal('9999999999.99'))]
PositiveId = Annotated[int, Field(gt=0, strict=True)]


class Contract(BaseModel):
    model_config = ConfigDict(extra='forbid', str_strip_whitespace=True)


class ItemEvidence(Contract):
    inspectionItemId: PositiveId
    deliveryItemId: PositiveId
    materialId: PositiveId | None
    materialName: Annotated[str, Field(max_length=200)] | None
    unit: Annotated[str, Field(max_length=50)] | None
    receivedQuantity: Quantity
    damagedQuantity: Quantity
    acceptedQuantity: Quantity
    rejectedQuantity: Quantity
    rejectionRate: Annotated[Decimal, Field(ge=0, le=1)]
    inspectionCoverage: Annotated[Decimal, Field(gt=0, le=1)]
    condition: Annotated[str, Field(max_length=100)] | None
    remarks: Annotated[str, Field(max_length=2000)] | None

    @model_validator(mode='after')
    def quantities(self):
        total = self.acceptedQuantity + self.rejectedQuantity
        if not 0 < total <= self.receivedQuantity:
            raise ValueError('Invalid inspected quantity')
        # ASP.NET supplies authoritative ratios; tolerate decimal serialization rounding only.
        if abs(self.rejectionRate - self.rejectedQuantity / total) > Decimal('0.00000001'):
            raise ValueError('Invalid rejection rate')
        if abs(self.inspectionCoverage - total / self.receivedQuantity) > Decimal('0.00000001'):
            raise ValueError('Invalid coverage')
        return self


class HistoryEvidence(Contract):
    inspectionId: PositiveId
    deliveryId: PositiveId
    inspectionDate: datetime
    overallDecision: Decision
    itemCount: Annotated[int, Field(ge=0)]
    rejectedItemCount: Annotated[int, Field(ge=0)]


class NcrEvidence(Contract):
    id: PositiveId
    inspectionItemId: PositiveId
    severity: Level
    status: Literal['Open', 'CorrectiveActionRequired', 'Resolved', 'Closed']
    issueDescription: Text
    correctiveAction: Annotated[str, Field(max_length=2000)] | None


class DiscrepancyEvidence(Contract):
    deliveryId: PositiveId
    status: Literal['DiscrepancyReported']


class IssueEvidence(Contract):
    id: PositiveId
    deliveryId: PositiveId
    issueType: Annotated[str, Field(max_length=100)]
    severity: Level
    description: Text


class EvidencePackage(Contract):
    inspectionId: PositiveId
    deliveryId: PositiveId
    supplierId: PositiveId
    supplierName: Annotated[str, Field(min_length=1, max_length=200)]
    supplierStatus: Literal['Active', 'Inactive', 'Suspended']
    inspectionDate: datetime
    overallDecision: Decision
    collectedAt: datetime
    items: Annotated[list[ItemEvidence], Field(min_length=1, max_length=100)]
    history: Annotated[list[HistoryEvidence], Field(max_length=20)]
    historyNote: Text
    priorNcrCount: Annotated[int, Field(ge=0)]
    priorNcrs: Annotated[list[NcrEvidence], Field(max_length=50)]
    ncrsTruncated: bool
    discrepancies: Annotated[list[DiscrepancyEvidence], Field(max_length=20)]
    discrepanciesTruncated: bool
    deliveryIssues: Annotated[list[IssueEvidence], Field(max_length=20)]
    deliveryIssuesTruncated: bool
    evidenceReferences: Annotated[list[Reference], Field(max_length=213)]

    @model_validator(mode='after')
    def scope(self):
        if len({i.inspectionItemId for i in self.items}) != len(self.items):
            raise ValueError('Duplicate inspection items')
        if len({i.deliveryItemId for i in self.items}) != len(self.items):
            raise ValueError('Duplicate delivery items')
        if any(h.inspectionId == self.inspectionId for h in self.history):
            raise ValueError('Current inspection cannot be history')
        return self


class RecommendationContract(Contract):
    model_config = ConfigDict(extra='forbid', strict=True, str_strip_whitespace=False)


class RiskFlag(RecommendationContract):
    flag: Annotated[str, Field(min_length=1, max_length=300), AfterValidator(require_nonblank)]
    evidenceReferences: Annotated[list[Reference], Field(min_length=1, max_length=20)]


class ItemRecommendation(RecommendationContract):
    inspectionItemId: PositiveId
    ncrRecommended: bool
    suggestedSeverity: Level | None
    suggestedIssueDescription: RecommendationText | None
    suggestedCorrectiveAction: RecommendationText | None
    rationale: RecommendationText
    evidenceReferences: Annotated[list[Reference], Field(min_length=1, max_length=20)]

    @model_validator(mode='after')
    def ncr_suggestions(self):
        suggestions = (self.suggestedSeverity, self.suggestedIssueDescription,
                       self.suggestedCorrectiveAction)
        if self.ncrRecommended and any(value is None for value in suggestions):
            raise ValueError('NCR recommendations require severity, issue description and corrective action')
        if not self.ncrRecommended and any(value is not None for value in suggestions):
            raise ValueError('Suggestion fields must be null when an NCR is not recommended')
        return self


class QualityRiskRecommendation(RecommendationContract):
    """Submit the final advisory assessment; this does not execute a business action."""
    inspectionId: PositiveId
    riskLevel: Level
    riskFlags: Annotated[list[RiskFlag], Field(max_length=20)]
    evidenceSummary: RecommendationText
    ncrRecommended: bool
    itemRecommendations: Annotated[list[ItemRecommendation], Field(max_length=100)]
    rationaleSummary: RecommendationText


class TraceEntry(Contract):
    iteration: Annotated[int, Field(ge=1, le=6)]
    action: Literal['get_current_inspection_evidence', 'get_supplier_quality_history',
                    'get_prior_non_conformance_summary', 'final_output']
    arguments: dict[str, int]
    success: bool
    durationMs: Annotated[float, Field(ge=0)]


class AgentResult(Contract):
    success: bool
    recommendation: QualityRiskRecommendation | None
    trace: Annotated[list[TraceEntry], Field(max_length=13)]
    iterationCount: Annotated[int, Field(ge=0, le=6)]
    modelIdentifier: Annotated[str, Field(min_length=1, max_length=100)]
    errorCode: str | None

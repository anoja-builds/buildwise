from typing import Annotated
from langchain_core.tools import tool
from pydantic import Field
from .models import Contract, EvidencePackage, HistoryEvidence, ItemEvidence, NcrEvidence, DiscrepancyEvidence, IssueEvidence


class NoArguments(Contract):
    pass


class HistoryArguments(Contract):
    limit: Annotated[int, Field(ge=1, le=20, strict=True)]


class CurrentObservation(Contract):
    inspectionId: int
    deliveryId: int
    supplierId: int
    supplierName: str
    supplierStatus: str
    overallDecision: str
    items: list[ItemEvidence]
    evidenceReferences: list[str]


class HistoryObservation(Contract):
    history: list[HistoryEvidence]
    historyNote: str
    evidenceReferences: list[str]


class NcrObservation(Contract):
    priorNcrCount: int
    priorNcrs: list[NcrEvidence]
    ncrsTruncated: bool
    discrepancies: list[DiscrepancyEvidence]
    discrepanciesTruncated: bool
    deliveryIssues: list[IssueEvidence]
    deliveryIssuesTruncated: bool
    evidenceReferences: list[str]


def build_tools(evidence: EvidencePackage):
    # Request-local closures, never global mutable evidence or database clients.
    @tool(args_schema=NoArguments)
    def get_current_inspection_evidence() -> dict:
        """Read the current authoritative inspection, quantities, ratios and supplier identity."""
        return CurrentObservation(
            inspectionId=evidence.inspectionId, deliveryId=evidence.deliveryId,
            supplierId=evidence.supplierId, supplierName=evidence.supplierName,
            supplierStatus=evidence.supplierStatus, overallDecision=evidence.overallDecision,
            items=evidence.items, evidenceReferences=[f'inspection:{evidence.inspectionId}',
                f'delivery:{evidence.deliveryId}', f'supplier:{evidence.supplierId}']
                + [f'inspection-item:{i.inspectionItemId}' for i in evidence.items]
        ).model_dump(mode='json')

    @tool(args_schema=HistoryArguments)
    def get_supplier_quality_history(limit: int) -> dict:
        """Read up to 20 earlier inspection events for this request's supplier only."""
        rows = evidence.history[:limit]
        return HistoryObservation(history=rows, historyNote=evidence.historyNote,
            evidenceReferences=[f'inspection:{h.inspectionId}' for h in rows]).model_dump(mode='json')

    @tool(args_schema=NoArguments)
    def get_prior_non_conformance_summary() -> dict:
        """Read bounded recorded NCR and delivery discrepancy evidence; not a complete status history."""
        return NcrObservation(priorNcrCount=evidence.priorNcrCount, priorNcrs=evidence.priorNcrs,
            ncrsTruncated=evidence.ncrsTruncated, discrepancies=evidence.discrepancies,
            discrepanciesTruncated=evidence.discrepanciesTruncated, deliveryIssues=evidence.deliveryIssues,
            deliveryIssuesTruncated=evidence.deliveryIssuesTruncated,
            evidenceReferences=[f'ncr:{n.id}' for n in evidence.priorNcrs]
                + [f'delivery:{d.deliveryId}' for d in evidence.discrepancies]
                + [f'delivery-issue:{i.id}' for i in evidence.deliveryIssues]).model_dump(mode='json')

    return {t.name: t for t in [get_current_inspection_evidence,
        get_supplier_quality_history, get_prior_non_conformance_summary]}

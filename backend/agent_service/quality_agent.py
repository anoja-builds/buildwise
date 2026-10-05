"""
Quality Risk & Non-Conformance Agent

FastAPI microservice running on port 8004 that analyzes inspection results,
evaluates defect rates, and recommends corrective actions.
"""

from fastapi import FastAPI
from pydantic import BaseModel
from typing import List

app = FastAPI(
    title="Quality Risk & Non-Conformance Agent",
    description="Analyzes inspection results and generates quality risk assessments",
    version="1.0.0"
)


class InspectionItemRiskInput(BaseModel):
    """Input model for a single inspected item."""
    material_name: str
    inspected_qty: float
    rejected_qty: float
    accepted_qty: float
    rejection_reason: str = ""


class QualityRiskInput(BaseModel):
    """Input model for quality risk analysis."""
    delivery_id: int
    items: List[InspectionItemRiskInput]


class QualityRiskOutput(BaseModel):
    """Output model for quality risk analysis results."""
    risk_level: str
    requires_ncr: bool
    suggested_corrective_action: str
    risk_flags: List[str]
    total_inspected: float
    total_rejected: float
    rejection_rate_pct: float


class NcrRecommendationOutput(BaseModel):
    """Output model for NCR recommendations."""
    requires_ncr: bool
    ncr_severity: str
    recommended_action: str
    justification: str

import os
from env_loader import ROOT_ENV

GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY")

@app.get("/health")
def health_check():
    """Health check endpoint for monitoring."""
    return {
        "status": "healthy",
        "service": "quality_risk_agent",
        "agent": "QualityRiskAnalysisAgent",
        "allowed_tools": ["analyze_quality_risk", "recommend_ncr"],
        "llm_rationale_enabled": bool(GEMINI_API_KEY or os.environ.get("ANTHROPIC_API_KEY")),
    }



@app.post("/api/agent/analyze-quality-risk", response_model=QualityRiskOutput)
def analyze_quality_risk(payload: QualityRiskInput):
    """
    Analyze inspection results and assess quality risk level.

    Evaluates:
    - Overall rejection rate
    - Per-item defect patterns
    - Whether NCR generation is required
    """
    total_inspected = sum(item.inspected_qty for item in payload.items)
    total_rejected = sum(item.rejected_qty for item in payload.items)

    # Calculate rejection rate
    rejection_rate_pct = 0.0
    if total_inspected > 0:
        rejection_rate_pct = (total_rejected / total_inspected) * 100

    # Determine risk level based on rejection rate and absolute quantities
    risk_flags = []
    requires_ncr = total_rejected > 0

    if total_rejected > 50:
        risk_flags.append("HIGH_DEFECT_RATE")
        risk_level = "High"
    elif total_rejected > 20:
        risk_flags.append("ELEVATED_DEFECT_RATE")
        risk_level = "Medium"
    elif total_rejected > 0:
        risk_flags.append("MINOR_QUALITY_DEFECT")
        risk_level = "Medium"
    else:
        risk_level = "Low"

    # Additional flag for high rejection rate percentage
    if rejection_rate_pct > 20:
        risk_flags.append("HIGH_REJECTION_PERCENTAGE")
        if risk_level == "Low":
            risk_level = "Medium"

    # Generate corrective action suggestion
    if requires_ncr:
        if total_rejected > 50:
            corrective_action = (
                "Issue formal NCR to supplier immediately. Request batch replacement "
                "and conduct root cause analysis. Consider temporary supplier suspension "
                "if defect pattern persists."
            )
        else:
            corrective_action = (
                "Issue formal NCR to supplier for defective materials. Request credit note "
                "or replacement for rejected quantity."
            )
    else:
        corrective_action = "No corrective action required. Material meets quality standards."

    return QualityRiskOutput(
        risk_level=risk_level,
        requires_ncr=requires_ncr,
        suggested_corrective_action=corrective_action,
        risk_flags=risk_flags,
        total_inspected=total_inspected,
        total_rejected=total_rejected,
        rejection_rate_pct=round(rejection_rate_pct, 2)
    )


@app.post("/api/agent/recommend-ncr", response_model=NcrRecommendationOutput)
def recommend_ncr(payload: QualityRiskInput):
    """
    Generate a specific NCR recommendation based on inspection data.

    Provides:
    - Whether NCR generation is required
    - Recommended severity level
    - Recommended action
    - Justification for the recommendation
    """
    total_inspected = sum(item.inspected_qty for item in payload.items)
    total_rejected = sum(item.rejected_qty for item in payload.items)
    high_severity_items = [i for i in payload.items if i.rejected_qty > 50]
    critical_items = [i for i in payload.items if i.rejected_qty > 100]

    requires_ncr = total_rejected > 0

    if not requires_ncr:
        return NcrRecommendationOutput(
            requires_ncr=False,
            ncr_severity="Low",
            recommended_action="No NCR required - all materials passed inspection.",
            justification="Zero rejected quantity across all inspected items."
        )

    # Determine severity based on worst-case items
    if critical_items:
        ncr_severity = "Critical"
        recommended_action = (
            "IMMEDIATE ACTION REQUIRED: Issue Critical NCR. Stop all further deliveries "
            "from this supplier pending investigation. Escalate to Quality Manager and "
            "Procurement Director. Initiate root cause analysis within 24 hours."
        )
        justification = f"Critical defect level: {len(critical_items)} item(s) with >100 units rejected."
    elif high_severity_items:
        ncr_severity = "High"
        recommended_action = (
            "Issue High-severity NCR to supplier. Request immediate batch replacement. "
            "Schedule supplier quality review meeting. Monitor next 3 deliveries closely."
        )
        justification = f"High defect level: {len(high_severity_items)} item(s) with >50 units rejected."
    else:
        ncr_severity = "Medium"
        recommended_action = (
            "Issue Medium-severity NCR for rejected materials. Request credit note or "
            "replacement. Document defect pattern for supplier performance tracking."
        )
        justification = f"Moderate defect level: {total_rejected} total units rejected across {len(payload.items)} items."

    return NcrRecommendationOutput(
        requires_ncr=requires_ncr,
        ncr_severity=ncr_severity,
        recommended_action=recommended_action,
        justification=justification
    )


@app.get("/")
def root():
    """Root endpoint with service information."""
    return {
        "service": "Quality Risk & Non-Conformance Agent",
        "version": "1.0.0",
        "endpoints": {
            "health": "GET /health",
            "analyze_quality_risk": "POST /api/agent/analyze-quality-risk",
            "recommend_ncr": "POST /api/agent/recommend-ncr"
        }
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8004)
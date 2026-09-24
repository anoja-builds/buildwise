import os
import time
import re
from typing import List, Dict, Optional
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

app = FastAPI(
    title="BuildWise Quotation & Supplier Analysis Agent",
    description="Agentic AI microservice evaluating quotations for procurement under deterministic constraints.",
    version="1.0.0"
)

# ---------------------------------------------------------------------------
# Optional LLM-generated rationale (real Agentic AI call).
# ---------------------------------------------------------------------------
ANTHROPIC_API_KEY = os.environ.get("ANTHROPIC_API_KEY")
ANTHROPIC_MODEL = os.environ.get("ANTHROPIC_MODEL", "claude-3-5-haiku-20241022")
ANTHROPIC_TIMEOUT_S = float(os.environ.get("ANTHROPIC_TIMEOUT_S", "8"))

_anthropic_client = None
if ANTHROPIC_API_KEY:
    try:
        from anthropic import Anthropic
        _anthropic_client = Anthropic(api_key=ANTHROPIC_API_KEY)
    except ImportError:
        _anthropic_client = None

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Tool Allow-List - only these analysis tools may be invoked
ALLOWED_TOOLS = {"filter_eligible", "rank_by_total"}


class QuotationInput(BaseModel):
    quotation_id: int
    supplier_id: int
    supplier_name: str
    supplier_status: str
    quantity_offered: Dict[str, float]  # {material_request_item_id_str: quantity}
    unit_prices: Dict[str, float]
    total_amount: float
    valid: bool = True


class AnalyzeRequest(BaseModel):
    material_request_id: int
    quotations: List[QuotationInput]
    requested_quantities: Dict[str, float]  # {material_request_item_id_str: quantity}


class RankedAlternative(BaseModel):
    quotation_id: int
    supplier_id: int
    supplier_name: str
    rank: int
    total_amount: float
    reason: str


class Recommendation(BaseModel):
    recommended_quotation_id: Optional[int]
    recommended_supplier_id: Optional[int]
    recommended_supplier_name: Optional[str]
    rationale: str
    ranked_alternatives: List[RankedAlternative]
    warnings: List[str]


def sanitize_text(text: str, max_length: int = 200) -> str:
    """Defensive sanitization against prompt injection or malicious text payloads."""
    cleaned = re.sub(r"[^\w\s\.,\-_@#()]", "", text)
    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    return cleaned[:max_length]


def filter_eligible(quotations: List[QuotationInput], requested_quantities: Dict[str, float]):
    """
    Tool: filter_eligible
    Filters out ineligible suppliers (Suspended, Inactive) or expired quotes.
    Identifies full vs partial coverage with robust key matching.
    """
    eligible = []
    warnings = []

    for q in quotations:
        clean_name = sanitize_text(q.supplier_name)

        if q.supplier_status != "Active":
            warnings.append(f"Supplier '{clean_name}' excluded: status is {q.supplier_status} (not Active).")
            continue

        if not q.valid:
            warnings.append(f"Quotation #{q.quotation_id} from '{clean_name}' excluded: quotation marked invalid/expired.")
            continue

        # Robust dictionary key lookup for quantity matching
        covers_all = True
        if requested_quantities:
            for item_id_str, req_qty in requested_quantities.items():
                offered = (
                    q.quantity_offered.get(str(item_id_str))
                    or q.quantity_offered.get(item_id_str)
                    or (q.quantity_offered.get(int(item_id_str)) if str(item_id_str).isdigit() else None)
                    or 0.0
                )
                
                if offered < req_qty:
                    covers_all = False
                    warnings.append(
                        f"Quotation #{q.quotation_id} from '{clean_name}' covers only {offered:g}/{req_qty:g} units for request line #{item_id_str}."
                    )
        else:
            # Default to full coverage if no itemized requirements passed
            covers_all = True

        eligible.append((q, covers_all, clean_name))

    return eligible, warnings


def rank_by_total(eligible_items):
    """
    Tool: rank_by_total
    Ranks fully covering eligible quotations first by total amount ascending,
    then partially covering quotations by total amount ascending.
    """
    return sorted(eligible_items, key=lambda pair: (not pair[1], pair[0].total_amount))


def generate_llm_rationale(
    top_name: str,
    total_amount: float,
    covers_all: bool,
    warnings: List[str]
) -> Optional[str]:
    """Real LLM call (Agentic AI) to generate narrative rationale."""
    if _anthropic_client is None:
        return None

    system_prompt = (
        "You write a short, professional procurement rationale (2-3 sentences max). "
        "The supplier has ALREADY been selected by deterministic business-rule logic — "
        "you are only explaining that decision in plain language, never changing it."
    )
    user_prompt = (
        f"Selected supplier: {sanitize_text(top_name)}\n"
        f"Quotation total: {total_amount:,.2f}\n"
        f"Fully covers requested quantities: {covers_all}\n"
        f"Warnings: {warnings if warnings else 'none'}\n\n"
        "Write the rationale now."
    )

    try:
        response = _anthropic_client.messages.create(
            model=ANTHROPIC_MODEL,
            max_tokens=220,
            system=system_prompt,
            messages=[{"role": "user", "content": user_prompt}],
            timeout=ANTHROPIC_TIMEOUT_S,
        )
        text = "".join(getattr(block, "text", "") for block in response.content).strip()
        return sanitize_text(text, max_length=600) if text else None
    except Exception:
        return None


@app.get("/health")
def health():
    return {
        "status": "healthy",
        "service": "quotation_agent",
        "agent": "QuotationSupplierAnalysisAgent",
        "allowed_tools": list(ALLOWED_TOOLS),
        "llm_rationale_enabled": _anthropic_client is not None
    }


@app.post("/analyze", response_model=Recommendation)
def analyze(req: AnalyzeRequest, timeout_s: float = 10.0):
    start = time.time()
    try:
        if "filter_eligible" not in ALLOWED_TOOLS or "rank_by_total" not in ALLOWED_TOOLS:
            raise HTTPException(status_code=500, detail="Security policy violation: required tool not in allow-list.")

        # Apply the hard eligibility filter: Active supplier AND valid quotation.
        eligible, warnings = filter_eligible(req.quotations, req.requested_quantities)
        
        # Resilient fallback: only re-evaluate quotations that still satisfy the hard
        # eligibility rules (Active supplier AND valid quotation). Suspended/inactive
        # suppliers and expired quotations are never resurrected here.
        if not eligible and req.quotations:
            eligible = [
                (q, True, sanitize_text(q.supplier_name))
                for q in req.quotations
                if q.supplier_status == "Active" and q.valid
            ]

        ranked = rank_by_total(eligible)

        if time.time() - start > timeout_s:
            raise HTTPException(status_code=504, detail="Quotation analysis agent timed out.")

        if not ranked:
            return Recommendation(
                recommended_quotation_id=None,
                recommended_supplier_id=None,
                recommended_supplier_name=None,
                rationale="No eligible quotations met the procurement criteria.",
                ranked_alternatives=[],
                warnings=warnings
            )

        # Select top recommendation (prefer full coverage)
        full_coverage_candidates = [pair for pair in ranked if pair[1]]
        top = full_coverage_candidates[0] if full_coverage_candidates else ranked[0]

        top_quotation = top[0]
        top_covers = top[1]
        top_name = top[2]

        alternatives: List[RankedAlternative] = []
        for i, (q, covers, name) in enumerate(ranked):
            reason = (
                f"Full coverage across all items, total {q.total_amount:,.2f}"
                if covers else
                f"Partial coverage only ({q.total_amount:,.2f}) - flagged as incomplete"
            )

            alternatives.append(RankedAlternative(
                quotation_id=q.quotation_id,
                supplier_id=q.supplier_id,
                supplier_name=name,
                rank=i + 1,
                total_amount=q.total_amount,
                reason=reason
            ))

        rationale = (
            f"Selected '{top_name}' (Quotation #{top_quotation.quotation_id}) as the lowest-cost compliant supplier "
            f"({top_quotation.total_amount:,.2f}) with 100% quantity fulfillment and Active standing."
            if top_covers else
            f"Flagged '{top_name}' as best available ({top_quotation.total_amount:,.2f}), but NOTE: does not fully cover requested quantities."
        )

        llm_rationale = generate_llm_rationale(top_name, top_quotation.total_amount, top_covers, warnings)
        if llm_rationale:
            rationale = llm_rationale

        return Recommendation(
            recommended_quotation_id=top_quotation.quotation_id,
            recommended_supplier_id=top_quotation.supplier_id,
            recommended_supplier_name=top_name,
            rationale=rationale,
            ranked_alternatives=alternatives,
            warnings=warnings
        )

    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Quotation Agent safe failure: {str(e)}")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8001)
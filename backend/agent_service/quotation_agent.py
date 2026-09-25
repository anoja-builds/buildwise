import os
import time
import re
from datetime import date
from typing import List, Dict, Optional
from env_loader import ROOT_ENV  # loads repository-root .env for standalone execution
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

app = FastAPI(
    title="BuildWise Quotation & Supplier Analysis Agent",
    description="Agentic AI microservice evaluating quotations for procurement under deterministic constraints.",
    version="1.0.0"
)

# ---------------------------------------------------------------------------
import httpx

# ---------------------------------------------------------------------------
# Optional LLM-generated rationale (real Agentic AI call).
# ---------------------------------------------------------------------------
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY")
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
    promised_delivery_date: Optional[date] = None
    transport_charge: float = 0.0
    payment_terms: Optional[str] = None
    supplier_history: Dict[str, float] = {
        "delivery_count": 0, "on_time_delivery_count": 0, "discrepancy_count": 0,
        "inspection_count": 0, "rejected_quantity": 0, "inspected_quantity": 0, "ncr_count": 0,
    }


class AnalyzeRequest(BaseModel):
    material_request_id: int
    quotations: List[QuotationInput]
    requested_quantities: Dict[str, float]
    required_date: Optional[date] = None  # {material_request_item_id_str: quantity}


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
    justification: List[str] = Field(default_factory=list)
    risk_flags: List[str] = Field(default_factory=list)
    ranking: List[RankedAlternative] = Field(default_factory=list)


def sanitize_text(text: str, max_length: int = 200) -> str:
    """Defensive sanitization against prompt injection or malicious text payloads."""
    cleaned = re.sub(r"[^\w\s\.,\-_@#()]", "", text)
    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    return cleaned[:max_length]


def _history_metrics(q: QuotationInput) -> Dict[str, float]:
    h = q.supplier_history or {}
    deliveries = float(h.get("delivery_count", 0) or 0)
    on_time = float(h.get("on_time_delivery_count", 0) or 0)
    discrepancies = float(h.get("discrepancy_count", 0) or 0)
    inspected = float(h.get("inspected_quantity", 0) or 0)
    rejected = float(h.get("rejected_quantity", 0) or 0)
    return {
        "deliveries": deliveries,
        "on_time_rate": (on_time / deliveries * 100) if deliveries else 100.0,
        "discrepancy_rate": (discrepancies / deliveries * 100) if deliveries else 0.0,
        "rejection_rate": (rejected / inspected * 100) if inspected else 0.0,
        "ncr_count": float(h.get("ncr_count", 0) or 0),
    }


def _landed_cost(q: QuotationInput) -> float:
    return round(q.total_amount + max(0.0, q.transport_charge), 2)


def filter_eligible(quotations: List[QuotationInput], requested_quantities: Dict[str, float], required_date: Optional[date] = None):
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

        if required_date and q.promised_delivery_date and q.promised_delivery_date > required_date:
            warnings.append(
                f"Quotation #{q.quotation_id} from '{clean_name}' excluded: promised delivery {q.promised_delivery_date} is after required date {required_date}."
            )
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
    """Deterministic ranking: full coverage, then landed cost, then history quality."""
    def sort_key(pair):
        q, covers_all, _ = pair
        h = _history_metrics(q)
        quality = h["on_time_rate"] - h["discrepancy_rate"] - h["rejection_rate"] - (h["ncr_count"] * 2)
        return (not covers_all, _landed_cost(q), -quality, q.quotation_id)
    return sorted(eligible_items, key=sort_key)


def generate_llm_rationale(
    top_name: str,
    total_amount: float,
    covers_all: bool,
    warnings: List[str]
) -> Optional[str]:
    """Real LLM call (Agentic AI) to generate narrative rationale."""
    if not GEMINI_API_KEY and _anthropic_client is None:
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

    if GEMINI_API_KEY:
        try:
            url = f"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key={GEMINI_API_KEY}"
            payload = {
                "contents": [{"parts": [{"text": f"{system_prompt}\n\n{user_prompt}"}]}]
            }
            res = httpx.post(url, json=payload, timeout=8.0)
            if res.status_code == 200:
                data = res.json()
                parts = data.get("candidates", [])[0].get("content", {}).get("parts", [])
                text = "".join(p.get("text", "") for p in parts).strip()
                if text:
                    return sanitize_text(text, max_length=600)
        except Exception:
            pass

    if _anthropic_client is not None:
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

    return None


@app.get("/health")
def health():
    return {
        "status": "healthy",
        "service": "quotation_agent",
        "agent": "QuotationSupplierAnalysisAgent",
        "allowed_tools": list(ALLOWED_TOOLS),
        "llm_rationale_enabled": bool(GEMINI_API_KEY or _anthropic_client is not None),
        "env_file_loaded": ROOT_ENV.exists(),
    }


@app.post("/analyze", response_model=Recommendation)
def analyze(req: AnalyzeRequest, timeout_s: float = 10.0):
    start = time.time()
    try:
        if "filter_eligible" not in ALLOWED_TOOLS or "rank_by_total" not in ALLOWED_TOOLS:
            raise HTTPException(status_code=500, detail="Security policy violation: required tool not in allow-list.")

        # Apply the hard eligibility filter: Active supplier AND valid quotation.
        eligible, warnings = filter_eligible(req.quotations, req.requested_quantities, req.required_date)
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
                warnings=warnings,
                justification=[],
                risk_flags=["NO_ELIGIBLE_QUOTATION"],
                ranking=[]
            )

        # Select top recommendation (prefer full coverage)
        full_coverage_candidates = [pair for pair in ranked if pair[1]]
        top = full_coverage_candidates[0] if full_coverage_candidates else ranked[0]

        top_quotation = top[0]
        top_covers = top[1]
        top_name = top[2]

        alternatives: List[RankedAlternative] = []
        risk_flags: List[str] = []
        justification: List[str] = []
        for i, (q, covers, name) in enumerate(ranked):
            history = _history_metrics(q)
            landed = _landed_cost(q)
            reason = (
                f"Full coverage; landed cost {landed:,.2f}; on-time history {history['on_time_rate']:.1f}%"
                if covers else
                f"Partial coverage; landed cost {landed:,.2f}; flagged as incomplete"
            )
            if not covers: risk_flags.append(f"PARTIAL_QUANTITY:{name}")
            if history["discrepancy_rate"] > 0: risk_flags.append(f"DELIVERY_DISCREPANCY_HISTORY:{name}")
            if history["rejection_rate"] > 0 or history["ncr_count"] > 0: risk_flags.append(f"QUALITY_HISTORY_REVIEW:{name}")
            alternatives.append(RankedAlternative(
                quotation_id=q.quotation_id, supplier_id=q.supplier_id, supplier_name=name,
                rank=i + 1, total_amount=landed, reason=reason
            ))
            if i == 0:
                justification = [
                    f"Quotation #{q.quotation_id} is Active, {'fully covers' if covers else 'partially covers'} the requested items.",
                    f"Landed cost is LKR {landed:,.2f}, including transport charge LKR {max(0.0, q.transport_charge):,.2f}.",
                    f"Supplier history: {history['on_time_rate']:.1f}% on-time, {history['discrepancy_rate']:.1f}% discrepancy, {history['rejection_rate']:.1f}% rejection.",
                ]
                if q.promised_delivery_date and req.required_date:
                    justification.append(f"Promised delivery {q.promised_delivery_date} is within the required date {req.required_date}.")
                if q.payment_terms:
                    justification.append(f"Payment terms recorded: {q.payment_terms}.")

        rationale = (
            f"Selected '{top_name}' (Quotation #{top_quotation.quotation_id}) as the lowest landed-cost compliant supplier "
            f"({_landed_cost(top_quotation):,.2f}) with {'100%' if top_covers else 'partial'} quantity fulfillment and Active standing."
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
            warnings=warnings,
            justification=justification,
            risk_flags=list(dict.fromkeys(risk_flags)),
            ranking=alternatives
        )

    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Quotation Agent safe failure: {str(e)}")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8001)
import os
from env_loader import ROOT_ENV
from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI(title="Delivery Discrepancy Agent")

# Tool Allow-List - only these analysis tools may be invoked
ALLOWED_TOOLS = {"analyze_discrepancy"}
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY")


class DiscrepancyInput(BaseModel):
    ordered_qty: float
    received_qty: float
    damaged_qty: float


@app.get("/health")
def health_check():
    """Health check endpoint for monitoring."""
    return {
        "status": "healthy",
        "service": "delivery_agent",
        "agent": "DeliveryDiscrepancyAgent",
        "allowed_tools": list(ALLOWED_TOOLS),
        "llm_rationale_enabled": bool(GEMINI_API_KEY or os.environ.get("ANTHROPIC_API_KEY")),
    }


@app.post("/api/agent/analyze-discrepancy")
def analyze(payload: DiscrepancyInput):
    shortage = payload.received_qty < payload.ordered_qty
    damaged = payload.damaged_qty > 0
    return {
        "shortage_detected": shortage,
        "damage_detected": damaged,
        "summary": "Discrepancy flagged." if (shortage or damaged) else "Fully verified."
    }

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8003)

import os
from env_loader import ROOT_ENV
from fastapi import FastAPI
from pydantic import BaseModel
from typing import List

app = FastAPI(title="Request Validation Agent")

# Tool Allow-List - only these analysis tools may be invoked
ALLOWED_TOOLS = {"analyze_request"}
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY")


class RequestAnalysisInput(BaseModel):
    request_id: int
    project_name: str
    reason: str
    items_count: int
    total_quantity: float = 0


@app.get("/health")
def health_check():
    """Health check endpoint for monitoring."""
    return {
        "status": "healthy",
        "service": "request_agent",
        "agent": "RequestAnalysisAgent",
        "allowed_tools": list(ALLOWED_TOOLS),
        "llm_rationale_enabled": bool(GEMINI_API_KEY or os.environ.get("ANTHROPIC_API_KEY")),
    }


@app.post("/api/agent/analyze-request")
def analyze(payload: RequestAnalysisInput):
    flags = []
    if "urgent" in payload.reason.lower():
        flags.append("HIGH_URGENCY")
    if payload.items_count > 5:
        flags.append("BULK_ORDER")
    if payload.total_quantity >= 200:
        flags.append("LARGE_QUANTITY_ORDER")
    return {"request_id": payload.request_id, "flags": flags, "status": "Analyzed"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8002)

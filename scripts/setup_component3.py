#!/usr/bin/env python3
"""Setup script for Component 3: Delivery & Material Receiving Management"""

import os

# Get the project root
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def write_file(rel_path, content):
    """Write content to a file relative to project root."""
    full_path = os.path.join(PROJECT_ROOT, rel_path)
    os.makedirs(os.path.dirname(full_path), exist_ok=True)
    with open(full_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"✓ Written: {rel_path}")

# 1. DeliveryAgent Python microservice
delivery_agent_content = '''from fastapi import FastAPI
from pydantic import BaseModel
from typing import List

app = FastAPI(title="Delivery Discrepancy Agent")

class ItemDiscrepancyInput(BaseModel):
    material_name: str
    ordered_qty: float
    received_qty: float
    damaged_qty: float

class DiscrepancyAnalysisInput(BaseModel):
    po_number: str
    items: List[ItemDiscrepancyInput]

class DiscrepancyAnalysisOutput(BaseModel):
    shortage_detected: bool
    damage_detected: bool
    summary: str
    warnings: List[str]

@app.get("/health")
def health_check():
    return {"status": "healthy", "service": "delivery_discrepancy_agent"}

@app.post("/api/agent/analyze-discrepancy", response_model=DiscrepancyAnalysisOutput)
def analyze_discrepancy(payload: DiscrepancyAnalysisInput):
    shortage = False
    damage = False
    warnings = []

    for item in payload.items:
        if item.received_qty < item.ordered_qty:
            shortage = True
            diff = item.ordered_qty - item.received_qty
            warnings.append(f"Shortage of {diff} unit(s) for {item.material_name}.")

        if item.damaged_qty > 0:
            damage = True
            warnings.append(f"Damaged goods reported: {item.damaged_qty} unit(s) of {item.material_name}.")

    summary = "Delivery discrepancy detected." if (shortage or damage) else "Delivery fully matched purchase order."

    return DiscrepancyAnalysisOutput(
        shortage_detected=shortage,
        damage_detected=damage,
        summary=summary,
        warnings=warnings
    )
'''

write_file("backend/agent_service/delivery_agent.py", delivery_agent_content)

print("\n=== Component 3 Setup Complete ===")
print("Next steps:")
print("1. Start delivery agent: uvicorn delivery_agent:app --host 127.0.0.1 --port 8003")
print("2. Update Program.cs to register DeliveryService")
print("3. Run dotnet build to verify compilation")

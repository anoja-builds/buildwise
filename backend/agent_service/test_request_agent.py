import pytest
from fastapi.testclient import TestClient
from request_agent import app

client = TestClient(app)

def test_analyze_request_high_urgency():
    payload = {
        "request_id": 1,
        "project_name": "Test Project",
        "reason": "urgent need for materials",
        "items_count": 3
    }
    response = client.post("/api/agent/analyze-request", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["request_id"] == 1
    assert "HIGH_URGENCY" in data["flags"]
    assert data["status"] == "Analyzed"

def test_analyze_request_bulk_order():
    payload = {
        "request_id": 2,
        "project_name": "Test Project",
        "reason": "regular order",
        "items_count": 10
    }
    response = client.post("/api/agent/analyze-request", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert "BULK_ORDER" in data["flags"]

def test_analyze_request_normal():
    payload = {
        "request_id": 3,
        "project_name": "Test Project",
        "reason": "standard material request",
        "items_count": 2
    }
    response = client.post("/api/agent/analyze-request", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert len(data["flags"]) == 0

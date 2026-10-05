import pytest
from fastapi.testclient import TestClient
from delivery_agent import app

client = TestClient(app)

def test_analyze_discrepancy_shortage():
    payload = {
        "ordered_qty": 100.0,
        "received_qty": 90.0,
        "damaged_qty": 0.0
    }
    response = client.post("/api/agent/analyze-discrepancy", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["shortage_detected"] == True
    assert data["damage_detected"] == False
    assert "Discrepancy flagged" in data["summary"]

def test_analyze_discrepancy_damage():
    payload = {
        "ordered_qty": 100.0,
        "received_qty": 100.0,
        "damaged_qty": 5.0
    }
    response = client.post("/api/agent/analyze-discrepancy", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["shortage_detected"] == False
    assert data["damage_detected"] == True
    assert "Discrepancy flagged" in data["summary"]

def test_analyze_discrepancy_none():
    payload = {
        "ordered_qty": 100.0,
        "received_qty": 100.0,
        "damaged_qty": 0.0
    }
    response = client.post("/api/agent/analyze-discrepancy", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["shortage_detected"] == False
    assert data["damage_detected"] == False
    assert "Fully verified" in data["summary"]

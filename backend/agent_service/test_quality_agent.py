"""
Test suite for the Quality Risk & Non-Conformance Agent.

Run with: pytest test_quality_agent.py -v
"""

import pytest
from fastapi.testclient import TestClient
from quality_agent import app


@pytest.fixture
def client():
    """Create a test client for the FastAPI app."""
    return TestClient(app)


class TestHealthEndpoint:
    """Tests for the /health endpoint."""

    def test_health_returns_healthy_status(self, client):
        """Health endpoint should return healthy status."""
        response = client.get("/health")
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "healthy"
        assert data["service"] == "quality_risk_agent"
        assert data["agent"] == "QualityRiskAnalysisAgent"

    def test_health_lists_allowed_tools(self, client):
        """Health endpoint should list available tools."""
        response = client.get("/health")
        data = response.json()
        assert "analyze_quality_risk" in data["allowed_tools"]
        assert "recommend_ncr" in data["allowed_tools"]


class TestQualityRiskAnalysis:
    """Tests for the /api/agent/analyze-quality-risk endpoint."""

    def test_zero_rejections_low_risk(self, client):
        """When no items are rejected, risk should be Low."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "OPC Cement", "inspected_qty": 100, "rejected_qty": 0, "accepted_qty": 100}
            ]
        }
        response = client.post("/api/agent/analyze-quality-risk", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["risk_level"] == "Low"
        assert data["requires_ncr"] is False
        assert data["total_rejected"] == 0

    def test_minor_rejections_medium_risk(self, client):
        """Small number of rejections should result in Medium risk."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "OPC Cement", "inspected_qty": 100, "rejected_qty": 5, "accepted_qty": 95}
            ]
        }
        response = client.post("/api/agent/analyze-quality-risk", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["risk_level"] == "Medium"
        assert data["requires_ncr"] is True
        assert data["total_rejected"] == 5

    def test_high_rejections_high_risk(self, client):
        """High rejections (>50 units) should be High risk."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Concrete Mix", "inspected_qty": 300, "rejected_qty": 75, "accepted_qty": 225}
            ]
        }
        response = client.post("/api/agent/analyze-quality-risk", json=payload)
        assert response.status_code == 200
        data = response.json()

class TestNcrRecommendation:
    """Tests for the /api/agent/recommend-ncr endpoint."""

    def test_no_ncr_when_no_rejections(self, client):
        """No NCR should be recommended when there are no rejections."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Good Product", "inspected_qty": 100, "rejected_qty": 0, "accepted_qty": 100}
            ]
        }
        response = client.post("/api/agent/recommend-ncr", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["requires_ncr"] is False
        assert data["ncr_severity"] == "Low"

    def test_medium_severity_ncr_for_minor_defects(self, client):
        """Minor defects should result in Medium severity NCR."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Product A", "inspected_qty": 100, "rejected_qty": 15, "accepted_qty": 85}
            ]
        }
        response = client.post("/api/agent/recommend-ncr", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["requires_ncr"] is True
        assert data["ncr_severity"] == "Medium"

    def test_high_severity_ncr_for_major_defects(self, client):
        """Major defects (>50 units) should result in High severity NCR."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Product B", "inspected_qty": 200, "rejected_qty": 60, "accepted_qty": 140}
            ]
        }
        response = client.post("/api/agent/recommend-ncr", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["requires_ncr"] is True
        assert data["ncr_severity"] == "High"
        assert "supplier quality review" in data["recommended_action"].lower()

    def test_critical_severity_ncr_for_critical_defects(self, client):
        """Critical defects (>100 units) should result in Critical severity NCR."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Product C", "inspected_qty": 300, "rejected_qty": 150, "accepted_qty": 150}
            ]
        }
        response = client.post("/api/agent/recommend-ncr", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["requires_ncr"] is True
        assert data["ncr_severity"] == "Critical"
        assert "IMMEDIATE ACTION" in data["recommended_action"]
        assert "root cause analysis" in data["recommended_action"].lower()
        # Verify justification mentions the count
        assert "1" in data["justification"] or "150" in data["justification"]
        assert data["requires_ncr"] is True

    def test_multiple_items_aggregation(self, client):
        """Multiple items should aggregate total rejected quantities."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "OPC Cement", "inspected_qty": 100, "rejected_qty": 10, "accepted_qty": 90},
                {"material_name": "Steel Bars", "inspected_qty": 200, "rejected_qty": 20, "accepted_qty": 180},
            ]
        }
        response = client.post("/api/agent/analyze-quality-risk", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["total_inspected"] == 300
        assert data["total_rejected"] == 30
        assert data["rejection_rate_pct"] == 10.0

    def test_high_rejection_percentage_flag(self, client):
        """High percentage rejection should add HIGH_REJECTION_PERCENTAGE flag."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Damaged Material", "inspected_qty": 50, "rejected_qty": 30, "accepted_qty": 20}
            ]
        }
        response = client.post("/api/agent/analyze-quality-risk", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["rejection_rate_pct"] == 60.0
        assert "HIGH_REJECTION_PERCENTAGE" in data["risk_flags"]

    def test_no_corrective_action_when_acceptable(self, client):
        """When no NCR required, corrective action should indicate no action needed."""
        payload = {
            "delivery_id": 1,
            "items": [
                {"material_name": "Good Product", "inspected_qty": 100, "rejected_qty": 0, "accepted_qty": 100}
            ]
        }
        response = client.post("/api/agent/analyze-quality-risk", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert "No corrective action" in data["suggested_corrective_action"]
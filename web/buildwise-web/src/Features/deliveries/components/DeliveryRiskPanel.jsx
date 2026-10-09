import React, { useState } from 'react';
import { deliveryService } from '../services/deliveryService';

export default function DeliveryRiskPanel({ purchaseOrderId, onEvaluated }) {
  const [loading, setLoading] = useState(false);
  const [assessment, setAssessment] = useState(null);
  const [error, setError] = useState('');

  const handleEvaluate = async () => {
    setLoading(true);
    setError('');
    try {
      // Simulate User ID 2 (Ramya Fernando, Receiving Officer) initiating the workflow
      const result = await deliveryService.evaluateRisk(purchaseOrderId, 2);
      setAssessment(result);
      if (onEvaluated) onEvaluated(result);
    } catch (err) {
      setError(err.message || 'Failed to evaluate risk.');
    } finally {
      setLoading(false);
    }
  };

  const getRiskBadgeClass = (level) => {
    switch (level?.toLowerCase()) {
      case 'high': return 'badge-danger';
      case 'medium': return 'badge-warning';
      default: return 'badge-success';
    }
  };

  return (
    <div className="card risk-agent-card" style={{ marginTop: '20px', borderLeft: '4px solid #9135ff' }}>
      <div className="card-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h4 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
          🤖 Delivery Risk Agent
        </h4>
        <button 
          className="btn btn--secondary" 
          onClick={handleEvaluate} 
          disabled={loading}
          style={{ padding: '6px 12px', fontSize: '13px' }}
        >
          {loading ? 'Evaluating...' : 'Run AI Evaluation'}
        </button>
      </div>
      <div className="card-body">
        {loading && (
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: '#666' }}>
            <div className="spinner"></div>
            <span>Agent analyzing promised dates vs required dates and historical supplier performance...</span>
          </div>
        )}
        
        {error && <div className="error-message" style={{ color: '#dc3545' }}>⚠️ {error}</div>}

        {!loading && !assessment && !error && (
          <p className="text-muted" style={{ fontSize: '13px', margin: 0 }}>
            Run the AI Agent to review quotation delivery risk, verify delivery date conflicts and analyze historical supplier records.
          </p>
        )}

        {assessment && (
          <div className="risk-results" style={{ animation: 'fadeIn 0.3s ease-in-out' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
              <span style={{ fontSize: '14px', fontWeight: 'bold' }}>Risk Level:</span>
              <span className={`badge ${getRiskBadgeClass(assessment.riskLevel)}`}>
                {assessment.riskLevel?.toUpperCase()} RISK
              </span>
            </div>

            <div style={{ marginBottom: '12px' }}>
              <span style={{ fontSize: '14px', fontWeight: 'bold', display: 'block', marginBottom: '4px' }}>AI Justification:</span>
              <div style={{ margin: 0, fontSize: '13.5px', lineHeight: '1.5', background: '#f8f9fa', padding: '10px', borderRadius: '6px' }}>
                {assessment.reasons && assessment.reasons.length > 0 && (
                  <>
                    <div style={{ fontWeight: 'bold', color: '#2c3e50', marginBottom: '4px' }}>Key Findings:</div>
                    <ul style={{ margin: '0 0 8px 0', paddingLeft: '20px' }}>
                      {assessment.reasons.map((r, i) => <li key={i}>{r}</li>)}
                    </ul>
                  </>
                )}
                {assessment.recommendation && (
                  <>
                    <div style={{ fontWeight: 'bold', color: '#2c3e50', marginBottom: '4px' }}>Recommendation:</div>
                    <p style={{ margin: 0, color: '#16a085' }}>{assessment.recommendation}</p>
                  </>
                )}
              </div>
            </div>

            {assessment.warnings && assessment.warnings.length > 0 && (
              <div>
                <span style={{ fontSize: '14px', fontWeight: 'bold', display: 'block', marginBottom: '6px', color: '#c0392b' }}>
                  Risk Warnings:
                </span>
                <ul style={{ margin: 0, paddingLeft: '20px', fontSize: '13px', color: '#c0392b' }}>
                  {assessment.warnings.map((warning, idx) => (
                    <li key={idx} style={{ marginBottom: '4px' }}>{warning}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

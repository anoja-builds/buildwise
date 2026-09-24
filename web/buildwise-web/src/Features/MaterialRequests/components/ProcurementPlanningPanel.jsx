import React, { useState } from 'react';
import { materialRequestService } from '../services/materialRequestService';

export default function ProcurementPlanningPanel({ requestId, onPlanningComplete }) {
  const [loading, setLoading] = useState(false);
  const [plan, setPlan] = useState(null);
  const [error, setError] = useState('');

  const handleRunAgent = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await materialRequestService.runPlanningAgent(requestId);
      setPlan(data);
      if (onPlanningComplete) onPlanningComplete(data);
    } catch (err) {
      setError(err.message || 'Error running Procurement Planning Agent');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="card" style={{ padding: '20px', borderLeft: '4px solid #2980b9', background: '#f4f8fb' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h4 style={{ margin: 0, color: '#1a5276', display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span>🤖</span> Procurement Planning Agent (AI Agent 1)
          </h4>
          <p style={{ margin: '4px 0 0 0', fontSize: '13px', color: '#566573' }}>
            Analyzes site demand, lead time urgency, volume risks, and formulates RFQ strategy.
          </p>
        </div>
        <button
          className="btn btn--primary"
          onClick={handleRunAgent}
          disabled={loading}
          style={{ background: '#2980b9', borderColor: '#2980b9' }}
        >
          {loading ? 'Analyzing Demand...' : '⚡ Run AI Planning Analysis'}
        </button>
      </div>

      {error && (
        <div style={{ marginTop: '12px', padding: '10px', background: '#fadbd8', color: '#78281f', borderRadius: '4px', fontSize: '13px' }}>
          ⚠️ {error}
        </div>
      )}

      {plan && (
        <div style={{ marginTop: '16px', borderTop: '1px dashed #aed6f1', paddingTop: '12px' }}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '12px' }}>
            <div>
              <strong style={{ fontSize: '12px', color: '#2874a6' }}>STRATEGY / RECOMMENDATION:</strong>
              <div style={{ fontSize: '14px', fontWeight: 'bold', color: '#1b4f72', marginTop: '2px' }}>
                {plan.recommendedAction}
              </div>
            </div>
            <div>
              <strong style={{ fontSize: '12px', color: '#2874a6' }}>OBJECTIVE:</strong>
              <div style={{ fontSize: '13px', color: '#2c3e50', marginTop: '2px' }}>
                {plan.objective}
              </div>
            </div>
          </div>

          {plan.riskFlags && plan.riskFlags.length > 0 && (
            <div style={{ marginBottom: '12px', background: '#fef9e7', padding: '10px', borderRadius: '4px', border: '1px solid #f9e79f' }}>
              <strong style={{ fontSize: '12px', color: '#b7950b', display: 'block', marginBottom: '4px' }}>
                ⚠️ DETECTED RISK FLAGS:
              </strong>
              <ul style={{ margin: 0, paddingLeft: '18px', fontSize: '12px', color: '#7d6608' }}>
                {plan.riskFlags.map((risk, i) => (
                  <li key={i}>{risk}</li>
                ))}
              </ul>
            </div>
          )}

          {plan.steps && (
            <div style={{ fontSize: '12.5px', color: '#34495e' }}>
              <strong>Execution Steps Formulated:</strong>
              <ol style={{ margin: '4px 0 0 0', paddingLeft: '18px' }}>
                {plan.steps.map((step, i) => (
                  <li key={i}>{step}</li>
                ))}
              </ol>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

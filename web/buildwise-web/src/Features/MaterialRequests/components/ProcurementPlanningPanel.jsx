import React, { useState } from 'react';
import { materialRequestService } from '../services/materialRequestService';
import { Button, Card, StatusBadge } from '../../../components/shared';

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
    <Card className="ai-planning-card" style={{ borderLeft: '4px solid var(--color-accent-600)', background: 'var(--color-white)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '16px', flexWrap: 'wrap' }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
            <span style={{ fontSize: '16px' }}>🤖</span>
            <h3 style={{ margin: 0, fontSize: '15px', fontWeight: 700, color: 'var(--color-text)' }}>
              Request Validation & Planning Agent (Agent 1)
            </h3>
            <StatusBadge status="Advisory" tone="warning" />
          </div>
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--color-text-muted)' }}>
            Analyzes project demand schedule, supplier lead-time constraints, and formulates automated procurement recommendations.
          </p>
        </div>
        <Button
          onClick={handleRunAgent}
          disabled={loading}
        >
          {loading ? 'Analyzing Demand…' : '⚡ Run AI Planning Analysis'}
        </Button>
      </div>

      <div style={{
        marginTop: '12px',
        padding: '8px 12px',
        background: 'var(--color-warning-100)',
        borderRadius: 'var(--radius-md)',
        border: '1px solid rgb(217 119 6 / 0.2)',
        fontSize: '12px',
        color: 'var(--color-warning-700)',
        display: 'flex',
        alignItems: 'center',
        gap: '6px'
      }}>
        <span>ℹ️</span>
        <span><strong>Advisory Notice:</strong> AI suggestions require Procurement Manager review. All recommendations are non-autonomous.</span>
      </div>

      {error && (
        <div className="auth-error-banner" style={{ marginTop: '12px' }} role="alert">
          {error}
        </div>
      )}

      {plan && (
        <div style={{ marginTop: '16px', borderTop: '1px solid var(--color-border)', paddingTop: '16px' }}>
          <div className="grid grid--2" style={{ marginBottom: '16px' }}>
            <div style={{ padding: '12px', background: 'var(--color-surface-muted)', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)' }}>
              <span style={{ fontSize: '11px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--color-accent-700)', display: 'block', marginBottom: '4px' }}>
                RECOMMENDED ACTION
              </span>
              <div style={{ fontSize: '14px', fontWeight: 700, color: 'var(--color-text)' }}>
                {plan.recommendedAction}
              </div>
            </div>

            <div style={{ padding: '12px', background: 'var(--color-surface-muted)', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)' }}>
              <span style={{ fontSize: '11px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--color-text-muted)', display: 'block', marginBottom: '4px' }}>
                PRIMARY OBJECTIVE
              </span>
              <div style={{ fontSize: '13px', color: 'var(--color-text)' }}>
                {plan.objective}
              </div>
            </div>
          </div>

          {plan.riskFlags && plan.riskFlags.length > 0 && (
            <div style={{ marginBottom: '16px', background: 'var(--color-warning-100)', padding: '12px', borderRadius: 'var(--radius-md)', border: '1px solid rgb(217 119 6 / 0.25)' }}>
              <strong style={{ fontSize: '12px', color: 'var(--color-warning-700)', display: 'block', marginBottom: '6px' }}>
                ⚠️ DETECTED RISK FLAGS ({plan.riskFlags.length})
              </strong>
              <ul style={{ margin: 0, paddingLeft: '18px', fontSize: '12px', color: 'var(--color-warning-700)' }}>
                {plan.riskFlags.map((risk, i) => (
                  <li key={i}>{risk}</li>
                ))}
              </ul>
            </div>
          )}

          {plan.steps && plan.steps.length > 0 && (
            <div style={{ fontSize: '13px', color: 'var(--color-text)' }}>
              <strong style={{ display: 'block', marginBottom: '6px' }}>Execution Sequence Formulated:</strong>
              <ol style={{ margin: 0, paddingLeft: '20px', color: 'var(--color-text-muted)' }}>
                {plan.steps.map((step, i) => (
                  <li key={i} style={{ marginBottom: '4px' }}>{step}</li>
                ))}
              </ol>
            </div>
          )}
        </div>
      )}
    </Card>
  );
}

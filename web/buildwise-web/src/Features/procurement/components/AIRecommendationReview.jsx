import { Card, EmptyState, StatusBadge } from '../../../components/shared'
import { statusTone } from './statusTone'

const stepDotTone = (status) => {
  if (status === 'Completed') return 'timeline-dot--completed'
  if (status === 'Failed') return 'timeline-dot--failed'
  if (status === 'Running') return 'timeline-dot--running'
  return ''
}

const stepGlyph = (status) => (status === 'Completed' ? '✓' : status === 'Failed' ? '!' : status === 'Running' ? '…' : '·')

export default function AIRecommendationReview({ workflow }) {
  if (!workflow) {
    return <Card title="AI Recommendation"><EmptyState title="No workflow yet" message="Run the AI analysis from the comparison view to get a recommendation." /></Card>
  }

  const { recommendation, validation, steps } = workflow

  return (
    <div className="stack">
      <Card className="ai-card" title="Quotation & Supplier Analysis Agent">
        <div className="actions" style={{ marginBottom: 'var(--space-3)' }}>
          <StatusBadge status={statusTone(workflow.status)}>{workflow.status}</StatusBadge>
          {validation && <StatusBadge status={validation.isValid ? 'success' : 'danger'}>{validation.isValid ? 'Deterministic validation passed' : 'Deterministic validation failed'}</StatusBadge>}
        </div>

        {!recommendation || !recommendation.recommendedSupplierId ? (
          <EmptyState title="No eligible recommendation" message="The agent could not recommend a winner — check warnings and validation errors below." />
        ) : (
          <>
            <div className="ai-card__winner">
              <div>
                <div className="muted">Recommended</div>
                <div className="ai-card__winner-name">{recommendation.recommendedSupplierName}</div>
                <div className="muted">Quotation #{recommendation.recommendedQuotationId}</div>
              </div>
              {recommendation.rankedAlternatives?.[0] && <div className="ai-card__winner-total">{recommendation.rankedAlternatives[0].totalAmount.toLocaleString()}</div>}
            </div>
            <div className="ai-card__rationale"><strong>Rationale: </strong>{recommendation.rationale}</div>
          </>
        )}

        {recommendation?.warnings?.length > 0 && (
          <ul className="warning-list" style={{ marginBottom: 'var(--space-4)' }}>
            {recommendation.warnings.map((w, i) => <li className="warning-item" key={i}>⚠ {w}</li>)}
          </ul>
        )}

        {validation && !validation.isValid && validation.errors?.length > 0 && (
          <ul className="warning-list">
            {validation.errors.map((e, i) => <li className="warning-item" key={i} style={{ background: 'var(--color-danger-100)', color: 'var(--color-danger-700)' }}>✕ {e}</li>)}
          </ul>
        )}

        {recommendation?.rankedAlternatives?.length > 0 && (
          <>
            <h3 style={{ marginTop: 'var(--space-5)' }}>Ranked alternatives</h3>
            <ul className="alt-list">
              {recommendation.rankedAlternatives.map((alt) => (
                <li className="alt-item" key={alt.quotationId}>
                  <span className="alt-item__rank">#{alt.rank}</span>
                  <div style={{ flex: 1 }}>
                    <strong>{alt.supplierName}</strong> — quotation #{alt.quotationId}
                    <div className="muted">{alt.reason}</div>
                  </div>
                  <div style={{ fontWeight: 700 }}>{alt.totalAmount.toLocaleString()}</div>
                </li>
              ))}
            </ul>
          </>
        )}
      </Card>

      <Card title="Execution history" subtitle="Step-by-step audit trail for this workflow run.">
        {!steps || steps.length === 0 ? <EmptyState title="No steps recorded" message="Execution history will appear once the workflow runs." /> : (
          <ul className="timeline">
            {steps.map((step) => (
              <li className="timeline-item" key={step.id}>
                <span className={`timeline-dot ${stepDotTone(step.status)}`}>{stepGlyph(step.status)}</span>
                <div>
                  <p><strong>{step.stepName}</strong> <span className="muted">({step.agentRole})</span></p>
                  <span className="activity-time">{step.status}{step.completedAt ? ` · ${new Date(step.completedAt).toLocaleString()}` : ''}</span>
                  {step.errorMessage && <div className="field__error">{step.errorMessage}</div>}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}

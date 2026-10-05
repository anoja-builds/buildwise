import { StatusBadge } from '../../../components/shared'
import { RISK_TONE, humanize } from '../shared/qualityFormat'

/**
 * Renders the QualityRiskAnalysisAgent result for one inspection.
 *
 * Deliberately states the advisory boundary: the agent recommends a risk level
 * and corrective action, while the inspection decision and the NCRs it is
 * linked to are the authoritative records produced by the backend and the
 * inspector.
 */
export default function QualityRiskPanel({ analysis }) {
  const flags = analysis.riskFlags ?? []
  return (
    <div className="stack" data-testid="quality-risk-panel">
      <div>
        <strong>AI Quality Risk Analysis</strong>{' '}
        <span className="muted">
          — {analysis.agent} ({analysis.tool}). Advisory only: it does not change this
          inspection or create the NCR; the backend and inspector decide the record.
        </span>
      </div>
      <div className="detail-row"><span>Agent</span><strong>{analysis.agent}</strong></div>
      <div className="detail-row"><span>Tool</span><strong>{analysis.tool}</strong></div>
      <div className="detail-row"><span>Execution source</span><StatusBadge status="info">{analysis.executionSource}</StatusBadge></div>
      <div className="detail-row"><span>Agent status</span><StatusBadge status="success">Completed</StatusBadge></div>
      <div className="detail-row">
        <span>Risk level</span>
        <StatusBadge status={RISK_TONE[analysis.riskLevel] ?? 'neutral'}>{analysis.riskLevel}</StatusBadge>
      </div>
      <div className="detail-row">
        <span>NCR</span>
        <strong>{analysis.requiresNcr ? 'Required by this assessment' : 'Not required'}</strong>
      </div>
      <div className="detail-row"><span>Total inspected</span><strong>{analysis.totalInspected}</strong></div>
      <div className="detail-row"><span>Total rejected</span><strong>{analysis.totalRejected}</strong></div>
      <div className="detail-row"><span>Rejection rate</span><strong>{analysis.rejectionRatePct}%</strong></div>
      <div className="detail-row">
        <span>Risk flags</span>
        <strong>{flags.length ? flags.join(', ') : 'None'}</strong>
      </div>
      <div className="detail-row">
        <span>Recommended action</span>
        <strong>{analysis.suggestedCorrectiveAction}</strong>
      </div>
      <div className="detail-row">
        <span>Recorded inspection</span>
        <strong>{humanize(analysis.inspectionStatus)} · {humanize(analysis.overallDecision)}</strong>
      </div>
    </div>
  )
}
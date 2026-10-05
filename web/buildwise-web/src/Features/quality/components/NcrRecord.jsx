import { Card, StatusBadge } from '../../../components/shared'
import { SEVERITY_TONE, STATUS_TONE, dateTime, humanize, qty } from '../shared/qualityFormat'
import QualityChecklist from './QualityChecklist'

/**
 * The full non-conformance record. The list shows only a summary, but the
 * evidence chain behind an NCR — delivery → inspection → item → quantities →
 * review trail — is what a reviewer needs before deciding whether to close it,
 * so it is opened on demand rather than rendered for every NCR at once.
 *
 * The chain is rendered explicitly (each origin row shows its id) so the
 * connection between a rejected inspection line and the material that arrived
 * on that delivery is visible rather than implied.
 */
export default function NcrRecord({ ncr }) {
  const item = ncr.inspectionItem ?? {}
  const inspection = item.inspection ?? {}
  const delivery = inspection.delivery ?? {}
  return (
    <div className="stack">
      <div className="detail-columns">
        <Card title="Non-conformance">
          <div className="detail-row"><span>NCR number</span><strong>{ncr.ncrNumber}</strong></div>
          <div className="detail-row"><span>Status</span><StatusBadge status={STATUS_TONE[ncr.status] ?? 'neutral'}>{humanize(ncr.status)}</StatusBadge></div>
          <div className="detail-row"><span>Severity</span><StatusBadge status={SEVERITY_TONE[ncr.severity] ?? 'neutral'}>{ncr.severity}</StatusBadge></div>
          <div className="detail-row"><span>Raised</span><span>{dateTime(ncr.createdAt)}</span></div>
          <div className="detail-row"><span>Quantity affected</span><span>{ncr.quantityAffected}</span></div>
        </Card>
        <Card title="Material & origin">
          <div className="detail-row"><span>Material</span><span>{item.material?.name ?? '—'}</span></div>
          <div className="detail-row"><span>Delivery</span><span>{delivery.id ? `DEL-${delivery.id}` : '—'}</span></div>
          <div className="detail-row"><span>Delivery ref</span><span>{delivery.deliveryReference ?? '—'}</span></div>
          <div className="detail-row"><span>Inspection</span><span>{inspection.id ? `INS-${inspection.id}` : '—'}</span></div>
          <div className="detail-row"><span>Decision</span><span>{humanize(inspection.overallDecision) || '—'}</span></div>
          <div className="detail-row">
            <span>Quality checklist</span>
            <span><QualityChecklist inspection={inspection} /></span>
          </div>
        </Card>
      </div>
      <Card title="Inspection quantities">
        <div className="detail-row"><span>Inspected</span><span>{qty(item.inspectedQuantity)}</span></div>
        <div className="detail-row"><span>Accepted</span><span>{qty(item.acceptedQuantity)}</span></div>
        <div className="detail-row"><span>Rejected</span><span>{qty(item.rejectedQuantity)}</span></div>
        <div className="detail-row"><span>Rejection reason</span><span>{item.rejectionReason ?? '—'}</span></div>
      </Card>
      <Card title="Issue & corrective action">
        <div className="detail-row"><span>Issue</span><span>{ncr.issueDescription ?? '—'}</span></div>
        <div className="detail-row"><span>Corrective action</span><span>{ncr.correctiveActionPlan ?? '—'}</span></div>
      </Card>
      <Card title="Review trail">
        <div className="detail-row"><span>Resolution</span><span>{ncr.resolution ?? 'Not yet resolved'}</span></div>
        <div className="detail-row"><span>Review notes</span><span>{ncr.reviewNotes ?? '—'}</span></div>
        <div className="detail-row"><span>Reviewed by</span><span>{ncr.reviewedByUserId ? `User ${ncr.reviewedByUserId}` : '—'}</span></div>
        <div className="detail-row"><span>Reviewed at</span><span>{dateTime(ncr.reviewedAt)}</span></div>
        <div className="detail-row"><span>Resolved at</span><span>{dateTime(ncr.resolvedAt)}</span></div>
        <div className="detail-row"><span>Closed at</span><span>{dateTime(ncr.closedAt)}</span></div>
      </Card>
    </div>
  )
}
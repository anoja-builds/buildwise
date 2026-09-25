import { useEffect, useState } from 'react'
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge, TextInput } from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'
import './common/common.css'

// Severity/status → badge tone, matching the shared badge scale
// (danger/warning/info/success/neutral) used across procurement screens.
const SEVERITY_TONE = {
  Critical: 'danger',
  High: 'danger',
  Medium: 'warning',
  Low: 'neutral',
}

const STATUS_TONE = {
  Open: 'info',
  UnderReview: 'info',
  CorrectiveActionRequired: 'warning',
  Resolved: 'success',
  Closed: 'neutral',
  AcceptedException: 'warning',
}

// Splits PascalCase enum values for display: "CorrectiveActionRequired" →
// "Corrective Action Required".
const humanize = (value) => value?.replace(/([A-Z])/g, ' $1').trim() ?? ''

export default function QualityInspectionsPage() {
  const { hasRole } = useAuth()
  const [ncrs, setNcrs] = useState([])
  const [inspections, setInspections] = useState([])
  const [review, setReview] = useState({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      const [ncrRows, inspectionRows] = await Promise.all([
        qualityApi.listNonConformances(),
        qualityApi.listInspections(),
      ])
      setNcrs(ncrRows)
      setInspections(inspectionRows)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  const canReview = hasRole('ProcurementManager') || hasRole('SiteManager') || hasRole('Administrator')

  async function transition(ncr) {
    const current = review[ncr.id] || { status: ncr.status === 'Open' ? 'UnderReview' : ncr.status === 'CorrectiveActionRequired' ? 'Resolved' : 'Closed', resolution: ncr.resolution || '', notes: '' }
    if (['Resolved', 'Closed', 'AcceptedException'].includes(current.status) && !current.resolution.trim()) {
      setError('A resolution is required for this NCR transition.')
      return
    }
    try {
      await qualityApi.transitionNonConformance(ncr.id, { status: current.status, reviewNotes: current.notes, resolution: current.resolution })
      await load()
    } catch (err) { setError(err.message) }
  }

  if (loading) return <LoadingState message="Loading quality inspections…" />
  if (error) return <ErrorState title="Could not load non-conformances" message={error} onRetry={load} />

  const openNcrs = ncrs.filter((n) => n.status !== 'Closed')
  const highSeverity = ncrs.filter((n) => n.severity === 'High' || n.severity === 'Critical')
  const resolved = ncrs.filter((n) => n.status === 'Resolved' || n.status === 'Closed')
  const inspectedItems = new Set(ncrs.map((n) => n.inspectionItemId)).size

  const summaries = [
    ['Open NCRs', String(openNcrs.length), 'Requiring attention', 'var(--color-danger-700)'],
    ['High/Critical Severity', String(highSeverity.length), 'Urgent resolution needed', 'var(--color-warning-700)'],
    ['Resolved NCRs', String(resolved.length), 'Corrective actions completed', 'var(--color-success-700)'],
    ['Flagged Inspection Items', String(inspectedItems), 'Items inspected with issues', 'var(--color-primary-700)'],
  ]

  return (
    <div className="stack">
      <PageHeader
        title="Quality Inspections & Non-Conformance"
        description="Track site inspection results, defect rates, and active Non-Conformance Reports (NCRs)."
      />

      <div className="grid grid--4">
        {summaries.map(([label, value, note, color]) => (
          <Card key={label} className="summary-card" style={{ '--summary-color': color }}>
            <div className="summary-card__label">{label}</div>
            <div className="summary-card__value">{value}</div>
            <div className="summary-card__note">{note}</div>
          </Card>
        ))}
      </div>

      <Card title="Inspection History" subtitle="Completed quality inspections linked to received deliveries.">
        {inspections.length === 0 ? (
          <EmptyState title="No inspections recorded" message="Completed Flutter inspections will appear here." />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>Inspection</th><th>Delivery</th><th>Decision</th><th>Criteria</th><th>Result</th><th>Evidence</th><th>Inspected</th></tr></thead>
              <tbody>
                {inspections.map((item) => (
                  <tr key={item.id}>
                    <td><strong>INS-{item.id}</strong></td>
                    <td>DEL-{item.deliveryId}</td>
                    <td><StatusBadge status={item.overallDecision === 'Accepted' ? 'success' : item.overallDecision === 'Rejected' ? 'danger' : 'warning'}>{humanize(item.overallDecision)}</StatusBadge></td>
                    <td>{item.inspectionCriteria || '—'}</td>
                    <td>{item.observedResult || '—'}</td>
                    <td>{item.evidence?.length || 0}</td>
                    <td>{item.inspectedAt ? new Date(item.inspectedAt).toLocaleString() : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card title="Active Non-Conformance Reports" subtitle="Non-conformances requiring tracking and corrective action.">
        {ncrs.length === 0 ? (
          <EmptyState
            title="No active non-conformances"
            message="All materials passed inspection — new NCRs appear here automatically when rejected quantities are recorded."
          />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>NCR</th>
                  <th>Material</th>
                  <th>Issue</th>
                  <th>Corrective Action</th>
                  <th>Created</th>
                  <th>Severity</th>
                  <th>Status</th>
                  <th>Review / Resolution</th>
                </tr>
              </thead>
              <tbody>
                {ncrs.map((ncr) => (
                  <tr key={ncr.id}>
                    <td><strong>{ncr.ncrNumber}</strong></td>
                    <td>{ncr.inspectionItem?.material?.name ?? '—'}</td>
                    <td>{ncr.issueDescription}</td>
                    <td className="muted">{ncr.correctiveActionPlan}</td>
                    <td>{ncr.createdAt ? new Date(ncr.createdAt).toLocaleDateString() : '—'}</td>
                    <td>
                      <StatusBadge status={SEVERITY_TONE[ncr.severity] ?? 'neutral'}>
                        {ncr.severity} Severity
                      </StatusBadge>
                    </td>
                    <td>
                      <StatusBadge status={STATUS_TONE[ncr.status] ?? 'neutral'}>
                        {humanize(ncr.status)}
                      </StatusBadge>
                    </td>
                    <td>
                      {canReview ? (
                        <div className="stack">
                          <select
                            aria-label={`Transition status for ${ncr.ncrNumber}`}
                            value={(review[ncr.id]?.status) || (ncr.status === 'Open' ? 'UnderReview' : ncr.status === 'CorrectiveActionRequired' ? 'Resolved' : 'Closed')}
                            onChange={(e) => setReview((current) => ({ ...current, [ncr.id]: { ...current[ncr.id], status: e.target.value } }))}
                          >
                            {['UnderReview', 'CorrectiveActionRequired', 'Resolved', 'AcceptedException', 'Closed'].map((status) => <option key={status} value={status}>{humanize(status)}</option>)}
                          </select>
                          <TextInput
                            label="Resolution"
                            value={review[ncr.id]?.resolution ?? ncr.resolution ?? ''}
                            onChange={(e) => setReview((current) => ({ ...current, [ncr.id]: { ...current[ncr.id], resolution: e.target.value } }))}
                            multiline
                          />
                          <TextInput
                            label="Review notes"
                            value={review[ncr.id]?.notes ?? ''}
                            onChange={(e) => setReview((current) => ({ ...current, [ncr.id]: { ...current[ncr.id], notes: e.target.value } }))}
                          />
                          <Button variant="secondary" onClick={() => transition(ncr)}>Save transition</Button>
                        </div>
                      ) : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}

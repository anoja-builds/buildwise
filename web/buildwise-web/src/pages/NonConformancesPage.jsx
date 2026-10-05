import { useEffect, useState } from 'react'
import { Button, Card, Drawer, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge, TextInput } from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'
import NcrRecord from '../Features/quality/components/NcrRecord'
import {
  NCR_TRANSITIONS,
  RESOLUTION_REQUIRED,
  SEVERITY_TONE,
  STATUS_TONE,
  humanize,
  suggestNextNcrStatus,
} from '../Features/quality/shared/qualityFormat'
import './common/common.css'

/**
 * COMPONENT 4 (second half) — non-conformance reports.
 *
 * Split from the former combined page. This half is the *review* workflow: an
 * NCR is raised automatically by the backend whenever a Quality Inspector
 * rejects an inspection line, and a Procurement Manager, Site Manager or
 * Administrator then drives it to resolution. Recording an inspection is a
 * different job with a different audience, so it lives on its own screen.
 */
export default function NonConformancesPage() {
  const { hasRole } = useAuth()
  const [ncrs, setNcrs] = useState([])
  const [review, setReview] = useState({})
  // Which NCR's full evidence chain is open in the side panel, if any.
  const [openNcrId, setOpenNcrId] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  // Which NCR's transition is in flight, so only that row's button disables.
  const [savingId, setSavingId] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setNcrs(await qualityApi.listNonConformances())
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  // Only approvers may move an NCR. The backend enforces the same rule, but
  // hiding the control keeps the UI honest about who can act.
  const canReview = hasRole('ProcurementManager') || hasRole('SiteManager') || hasRole('Administrator')

  // The record shown in the side panel. Derived from the already-loaded list so
  // opening a detail never needs another request, and a transition refreshes the
  // panel automatically.
  const openNcr = ncrs.find((ncr) => ncr.id === openNcrId) ?? null

  async function transition(ncr) {
    setSavingId(ncr.id)
    const current = review[ncr.id] || {
      status: suggestNextNcrStatus(ncr.status),
      resolution: ncr.resolution || '',
      notes: '',
    }
    // Mirrors the backend rule, so the reviewer is told before a round trip.
    //
    // The review object is built by the status select's onChange, which spreads
    // `{ ...c[ncr.id] }`. When the reviewer has not touched the resolution
    // field that spread is empty, so `current.resolution` is undefined and
    // calling .trim() on it threw a TypeError *outside* the try/catch. The
    // effect was a silent crash: the guard never reported anything and the
    // page looked unchanged. Coerce to a string first.
    const resolution = (current.resolution || '').trim()
    if (RESOLUTION_REQUIRED.includes(current.status) && !resolution) {
      setError('A resolution is required for this NCR transition.')
      // Clear the in-flight marker; the early return skips the finally below,
      // which would otherwise leave the row's button permanently disabled.
      setSavingId(null)
      return
    }
    try {
      await qualityApi.transitionNonConformance(ncr.id, {
        status: current.status,
        reviewNotes: current.notes,
        resolution,
      })
      setError(null)
      await load()
    } catch (err) { setError(err.message) }
    finally { setSavingId(null) }
  }
  if (loading) return <LoadingState message="Loading non-conformances…" />
  if (error && ncrs.length === 0) {
    return <ErrorState title="Could not load non-conformances" message={error} onRetry={load} />
  }
  // When NCRs are already on screen, `error` is not a load failure — it is a
  // message from an action the user just took (for example a resolution is
  // required for this transition). Rendering it only when the list is empty
  // swallowed those messages entirely, so the guard ran and the user was told
  // nothing. It is shown as an inline banner instead, leaving the loaded list
  // intact.
  const actionError = error && ncrs.length > 0 ? error : null

  const open = ncrs.filter((n) => n.status !== 'Closed')
  const highSeverity = ncrs.filter((n) => n.severity === 'High' || n.severity === 'Critical')
  const resolved = ncrs.filter((n) => ['Resolved', 'Closed', 'AcceptedException'].includes(n.status))

  const summaryCards = [
    ['Total NCRs', ncrs.length, 'Raised by rejected lines', '#2563eb'],
    ['Open / in review', open.length, 'Awaiting an outcome', '#b45309'],
    ['High or critical', highSeverity.length, 'Needs priority review', '#b91c1c'],
    ['Resolved', resolved.length, 'Reached an outcome', '#15803d'],
  ]

  return (
    <div className="stack">
      <PageHeader
        title="Non-Conformance Reports"
        description="Every rejected inspection line raises an NCR automatically. Review these to a resolution and close them."
      />

      {actionError && (
        <ErrorState title="Could not save that change" message={actionError} />
      )}

      <div className="grid grid--4">
        {summaryCards.map(([label, value, note, color]) => (
          <Card key={label} className="summary-card" style={{ '--summary-color': color }}>
            <div className="summary-card__label">{label}</div>
            <div className="summary-card__value">{value}</div>
            <div className="summary-card__note">{note}</div>
          </Card>
        ))}
      </div>

      <Card title="Non-Conformance Reports" subtitle="Reports requiring tracking, corrective action and resolution.">
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
                    <td>
                      {/* The NCR number opens the full evidence chain: the
                          delivery, inspection and line that caused it. */}
                      <Button variant="secondary" onClick={() => setOpenNcrId(ncr.id)}>
                        {ncr.ncrNumber}
                      </Button>
                    </td>
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
                            value={review[ncr.id]?.status ?? suggestNextNcrStatus(ncr.status)}
                            onChange={(e) => setReview((c) => ({ ...c, [ncr.id]: { ...c[ncr.id], status: e.target.value } }))}
                          >
                            {NCR_TRANSITIONS.map((s) => <option key={s} value={s}>{humanize(s)}</option>)}
                          </select>
                          <TextInput
                            label="Resolution"
                            value={review[ncr.id]?.resolution ?? ncr.resolution ?? ''}
                            onChange={(e) => setReview((c) => ({ ...c, [ncr.id]: { ...c[ncr.id], resolution: e.target.value } }))}
                            multiline
                          />
                          <TextInput
                            label="Review notes"
                            value={review[ncr.id]?.notes ?? ''}
                            onChange={(e) => setReview((c) => ({ ...c, [ncr.id]: { ...c[ncr.id], notes: e.target.value } }))}
                          />
                          <Button variant="primary" onClick={() => transition(ncr)} disabled={savingId === ncr.id}>
                            {savingId === ncr.id ? 'Saving…' : 'Save transition'}
                          </Button>
                        </div>
                      ) : (
                        <span className="muted">{ncr.resolution ?? 'Awaiting Procurement Manager review'}</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Drawer open={openNcr != null} title={openNcr?.ncrNumber ?? ''} onClose={() => setOpenNcrId(null)}>
        {openNcr && <NcrRecord ncr={openNcr} />}
      </Drawer>
    </div>
  )
}
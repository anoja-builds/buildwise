import { useCallback, useEffect, useState } from 'react'
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeader,
  StatusBadge,
  TextInput,
  SelectInput,
  isMaterialRequestDecidable,
  materialRequestTone,
  requestAnalysisFlagTone,
  requestAnalysisFlagLabel,
} from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'
import { getHolidayAdvisory, validateQuantity } from '../utils/sriLankaValidation'
import './common/common.css'

// Status tones and the "still decidable" rule live in
// components/shared/materialRequestStatus.js, shared with every other surface
// that reads material requests (procurement queue, dashboard, workspace, RFQ
// page) so one status can never be highlighted here and greyed out there.

// How often an open queue refreshes itself. The site team submits from their
// own session (another browser, or the Flutter app) into the same database, so
// a list fetched once at mount keeps showing the state it loaded at: the
// Procurement Manager's Material Requests page did not "update" when the Site
// Engineer created a request — the request was stored (it was visible in the
// site engineer's own list) but this page never asked the API again. The
// background refresh below closes that gap; 30s keeps the queue current
// without hammering the API.
const AUTO_REFRESH_MS = 30000

export default function MaterialRequestsPage() {
  const { hasRole } = useAuth()
  // Roles that may record Approve / Reject / Request Revision — mirrors the
  // backend MaterialRequestApprovalOnly policy.
  const canApprove = hasRole('ProcurementManager') || hasRole('SiteManager') || hasRole('Administrator')
  const canCreate = hasRole('SiteEngineer') || hasRole('Administrator')
  const isSiteUser = hasRole('SiteEngineer')
  // Procurement staff who may read the whole queue but cannot decide: the
  // Procurement Officer moves an Approved request through RFQ / quotation, so
  // the Approved rows are precisely the ones they need to see. They were
  // previously served the PendingApproval-only queue, which hid MR-52.
  const isProcurementReader = hasRole('ProcurementOfficer')
  const [mode, setMode] = useState('list')
  const [selected, setSelected] = useState(null)
  const [requests, setRequests] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [refreshedAt, setRefreshedAt] = useState(null)
  // Search + filter (client-side on already-loaded list)
  const [searchQuery, setSearchQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [priorityFilter, setPriorityFilter] = useState('all')


  /**
   * Loads the queue. A `background` refresh (poll, focus, or returning from the
   * create/review screens) never blanks the table and never replaces visible
   * rows with an error, so a failed poll cannot take the queue away from the
   * user; the next tick or the Refresh button retries.
   *
   * Memoized so the mount/auto-refresh effects depend on one stable value and
   * re-run only when the signed-in role set actually changes.
   */
  const loadRequests = useCallback(async ({ background = false } = {}) => {
    if (!background) {
      setLoading(true)
      setError(null)
    }
    try {
      const result = isSiteUser
        ? await qualityApi.listMyMaterialRequests()
        // Approvers ask for 'all' so every submitted request is reachable and a
        // decision (PendingApproval → Approved) stays visible in the list.
        // Asking for no status at all used to send `?status=`, which only worked
        // because the API happens to treat an empty value as "no filter" — that
        // is what hid freshly submitted PendingApproval rows from Procurement.
        // Read-only roles keep the pending queue.
        // Procurement readers (approvers and the Procurement Officer) ask for
        // 'all': the Officer's work starts once a request is Approved, so a
        // pending-only queue hid exactly the rows they act on (e.g. MR-52).
        : await qualityApi.listMaterialRequests(canApprove || isProcurementReader ? 'all' : undefined)
      setRequests(result)
      setRefreshedAt(new Date())
      setError(null)
    } catch (err) {
      if (!background) setError(err.message)
    } finally {
      if (!background) setLoading(false)
    }
  }, [canApprove, isSiteUser, isProcurementReader])

  // First paint loads with the spinner; every later pass is a background
  // refresh. Re-runs only if the signed-in role set changes.
  useEffect(() => {
    loadRequests()
  }, [loadRequests])

  // Keep the queue current while the page is open: poll while the tab is
  // visible, and refresh the moment it is focused again (the manager tabbing
  // back from the site engineer's screen) — never a manual browser reload.
  useEffect(() => {
    const refreshIfVisible = () => {
      if (document.visibilityState === 'visible') loadRequests({ background: true })
    }
    const timer = setInterval(refreshIfVisible, AUTO_REFRESH_MS)
    window.addEventListener('focus', refreshIfVisible)
    return () => {
      clearInterval(timer)
      window.removeEventListener('focus', refreshIfVisible)
      document.removeEventListener('visibilitychange', refreshIfVisible)
    }
  }, [loadRequests])

  if (mode === 'list') {
    // Client-side filtering on the already-loaded list
    const filteredRequests = requests.filter((r) => {
      const q = searchQuery.trim().toLowerCase()
      const matchesSearch = !q ||
        String(r.id).includes(q) ||
        (r.projectName || '').toLowerCase().includes(q) ||
        (r.reason || '').toLowerCase().includes(q)
      const matchesStatus = statusFilter === 'all' || r.status === statusFilter
      const matchesPriority = priorityFilter === 'all' || r.priority === priorityFilter
      return matchesSearch && matchesStatus && matchesPriority
    })

    return (
      <div className="stack">
        <div className="toolbar">
          <PageHeader
            title="Material Requests"
            description="Site teams submit material requests; managers review them here and Approve, Reject, or Request Revision."
          />
          <div className="toolbar__filters">
            {refreshedAt && (
              <span className="activity-time" role="status">
                Updated {refreshedAt.toLocaleTimeString()}
              </span>
            )}
            <button
              className="bw-button bw-button--secondary"
              disabled={loading}
              onClick={() => loadRequests({ background: true })}
            >
              ↻ Refresh
            </button>
            {canCreate ? (
              <button className="bw-button bw-button--primary" onClick={() => setMode('create')}>
                + Create Request
              </button>
            ) : null}
          </div>
        </div>

        {/* ── Search + filter bar ── */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: '1fr auto auto',
            gap: '0.75rem',
            alignItems: 'end',
            background: 'var(--color-surface-muted)',
            borderRadius: 'var(--radius-md)',
            padding: '0.85rem 1rem',
            border: '1px solid var(--color-border)',
          }}
        >
          {/* Search */}
          <div className="field">
            <label className="field__label">Search</label>
            <div style={{ position: 'relative' }}>
              <span style={{ position: 'absolute', left: '0.75rem', top: '50%', transform: 'translateY(-50%)', color: 'var(--color-text-muted)', pointerEvents: 'none' }}>
                🔍
              </span>
              <input
                className="field__control"
                type="search"
                placeholder="Search by ID, project name, or reason…"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                style={{ paddingLeft: '2.2rem' }}
              />
            </div>
          </div>

          {/* Status filter */}
          <div className="field">
            <label className="field__label">Status</label>
            <select
              className="field__control"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="all">All statuses</option>
              <option value="PendingApproval">Pending Approval</option>
              <option value="Approved">Approved</option>
              <option value="Rejected">Rejected</option>
              <option value="RevisionRequested">Revision Requested</option>
              <option value="Fulfilled">Fulfilled</option>
            </select>
          </div>

          {/* Priority filter */}
          <div className="field">
            <label className="field__label">Priority</label>
            <select
              className="field__control"
              value={priorityFilter}
              onChange={(e) => setPriorityFilter(e.target.value)}
            >
              <option value="all">All priorities</option>
              <option value="Urgent">Urgent</option>
              <option value="High">High</option>
              <option value="Normal">Normal</option>
              <option value="Low">Low</option>
            </select>
          </div>
        </div>

        {/* result count */}
        {(searchQuery || statusFilter !== 'all' || priorityFilter !== 'all') && (
          <p style={{ fontSize: 'var(--font-sm)', color: 'var(--color-text-muted)', margin: 0 }}>
            Showing {filteredRequests.length} of {requests.length} requests
            {' '}
            <button
              style={{ color: 'var(--color-primary-700)', fontWeight: 600, background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline', fontSize: 'inherit' }}
              onClick={() => { setSearchQuery(''); setStatusFilter('all'); setPriorityFilter('all') }}
            >
              Clear filters
            </button>
          </p>
        )}

        {loading && <LoadingState />}
        {error && <ErrorState message={error} onRetry={loadRequests} />}
        {!loading && !error && (
          <div
            style={{
              background: 'var(--color-white)',
              borderRadius: 'var(--radius-lg)',
              border: '1px solid var(--color-border)',
              boxShadow: '0 1px 2px rgb(19 26 43 / 0.04)',
              overflow: 'hidden',
            }}
          >
            {filteredRequests.length === 0 ? (
              <EmptyState
                title={requests.length === 0 ? 'No material requests' : 'No matches'}
                message={
                  requests.length === 0
                    ? canApprove ? 'Requests awaiting your approval will appear here.' : 'Submitted requests will appear here.'
                    : 'Try adjusting your search or filters.'
                }
              />
            ) : (
              <div className="table-wrap">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>#</th>
                      <th>Project</th>
                      <th>Required Date</th>
                      <th>Priority</th>
                      <th>Items</th>
                      <th>Status</th>
                      {(canApprove || isSiteUser || isProcurementReader) && <th>Actions</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {filteredRequests.map((r) => (
                      <tr key={r.id}>
                        <td style={{ fontWeight: 700, color: 'var(--color-primary-700)' }}>{r.id}</td>
                        <td style={{ fontWeight: 600 }}>{r.projectName || `Project #${r.projectId}`}</td>
                        <td>{r.requiredDate}</td>
                        <td>
                          <span
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              borderRadius: '999px',
                              padding: '0.2rem 0.65rem',
                              fontWeight: 700,
                              fontSize: '0.78rem',
                              background: r.priority === 'Urgent' ? 'var(--color-danger-100)'
                                : r.priority === 'High' ? 'var(--color-warning-100)'
                                : r.priority === 'Normal' ? 'var(--color-info-100)' : '#eef2f6',
                              color: r.priority === 'Urgent' ? 'var(--color-danger-700)'
                                : r.priority === 'High' ? 'var(--color-warning-700)'
                                : r.priority === 'Normal' ? 'var(--color-info-700)' : 'var(--color-text-muted)',
                            }}
                          >
                            {r.priority}
                          </span>
                        </td>
                        <td>{r.itemCount}</td>
                        <td>
                          <StatusBadge status={materialRequestTone(r.status)}>
                            {r.status}
                          </StatusBadge>
                        </td>
                        {(isSiteUser || isProcurementReader) && (
                          <td>
                            <Button
                              variant="secondary"
                              onClick={() => {
                                setSelected(r)
                                setMode('review')
                              }}
                            >
                              View
                            </Button>
                          </td>
                        )}
                        {canApprove && (
                          <td>
                            <Button
                              variant="secondary"
                              onClick={() => {
                                setSelected(r)
                                setMode('review')
                              }}
                            >
                              {isMaterialRequestDecidable(r.status) ? 'Review' : 'View & Analyze'}
                            </Button>
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>
    )
  }


  if (mode === 'review' && selected) {
    return (
      <ReviewRequest
        request={selected}
        canApprove={canApprove}
        isSiteUser={isSiteUser}
        isProcurementReader={isProcurementReader}
        onBack={() => {
          setSelected(null)
          setMode('list')
          // Returning to the queue re-reads it (silently, so the table does not
          // flash) — the decision just recorded is reflected immediately.
          loadRequests({ background: true })
        }}
      />
    )
  }

  if (mode === 'create') {
    return (
      <CreateRequestForm
        onCancel={() => {
          setMode('list')
          // Returning from the form re-reads the queue in the background, so
          // the request just created is already in the site engineer's own
          // list instead of only in the database.
          loadRequests({ background: true })
        }}
      />
    )
  }

  return null
}

// ------------------------------------------------------------------ Form

function CreateRequestForm({ onCancel }) {
  const [form, setForm] = useState({
    projectId: 1,
    requiredDate: '',
    requestDate: new Date().toISOString().slice(0, 10),
    priority: 'Normal',
    reason: '',
    siteNotes: '',
    materialId: 1,
    requestedQuantity: '',
    description: '',
    unit: 'bags',
  })
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState(null)
  const [submitOk, setSubmitOk] = useState(false)

  const projects = qualityApi.projects()
  const materials = qualityApi.materials()

  const update = (field, value) =>
    setForm((f) => ({ ...f, [field]: value }))

  async function handleSubmit(e) {
    e.preventDefault()
    setSubmitting(true)
    setSubmitError(null)
    setSubmitOk(false)

    const payload = {
      projectId: form.projectId,
      requestDate: form.requestDate,
      requiredDate: form.requiredDate,
      priority: form.priority,
      reason: form.reason || undefined,
      siteNotes: form.siteNotes || undefined,
      items: [{
        materialId: form.materialId,
        requestedQuantity: Number(form.requestedQuantity),
        unit: form.unit || undefined,
        description: form.description || undefined,
        requiredDate: form.requiredDate,
        notes: form.description || undefined,
      }],
    }

    try {
      await qualityApi.createMaterialRequest(payload)
      setSubmitOk(true)
      setForm({
        projectId: 1,
        requiredDate: '',
        requestDate: new Date().toISOString().slice(0, 10),
        priority: 'Normal',
        reason: '',
        siteNotes: '',
        materialId: 1,
        requestedQuantity: '',
        description: '',
        unit: 'bags',
      })
    } catch (err) {
      setSubmitError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  if (submitOk) {
    return (
      <Card>
        <EmptyState
          title="Request submitted"
          message="Your material request has been created and is now awaiting approval."
        />
        <div className="form-actions">
          <Button variant="secondary" onClick={onCancel}>
            ← Back to list
          </Button>
        </div>
      </Card>
    )
  }

  return (
    <form className="form-layout" onSubmit={handleSubmit}>
      <PageHeader
        title="Create Material Request"
        description="Submit a new material request for procurement approval."
      />

      {submitError && <ErrorState message={submitError} />}

      <Card>
        <div className="field-grid">
          <SelectInput
            label="Project"
            id="projectId"
            required
            value={form.projectId}
            onChange={(e) => update('projectId', Number(e.target.value))}
            options={projects.map((p) => ({ value: p.id, label: p.name }))}
          />

          <TextInput
            label="Request Date"
            id="requestDate"
            type="date"
            required
            value={form.requestDate}
            onChange={(e) => update('requestDate', e.target.value)}
          />

          <TextInput
            label="Required Date"
            id="requiredDate"
            type="date"
            required
            value={form.requiredDate}
            onChange={(e) => update('requiredDate', e.target.value)}
          />

          <SelectInput
            label="Priority"
            id="priority"
            required
            value={form.priority}
            onChange={(e) => update('priority', e.target.value)}
            options={[
              { value: 'Low', label: 'Low' },
              { value: 'Normal', label: 'Normal' },
              { value: 'High', label: 'High' },
              { value: 'Urgent', label: 'Urgent' },
            ]}
          />

          <div className="field-full">
            <TextInput
              label="Site Notes"
              id="siteNotes"
              value={form.siteNotes}
              onChange={(e) => update('siteNotes', e.target.value)}
              hint="Access, unloading, storage, or site coordination notes."
            />
          </div>

          <div className="field-full">
            <TextInput
              label="Reason / Purpose"
              id="reason"
              value={form.reason}
              onChange={(e) => update('reason', e.target.value)}
              hint="Brief description of why the material is needed."
            />
          </div>

          <SelectInput
            label="Material"
            id="materialId"
            required
            value={form.materialId}
            onChange={(e) => update('materialId', Number(e.target.value))}
            options={materials.map((m) => ({ value: m.id, label: m.name }))}
          />

          <TextInput
            label="Unit"
            id="unit"
            required
            value={form.unit}
            onChange={(e) => update('unit', e.target.value)}
            hint="For example: bags, kg, m³, pieces."
          />

          <TextInput
            label="Quantity"
            id="requestedQuantity"
            type="number"
            inputMode="decimal"
            required
            value={form.requestedQuantity}
            onChange={(e) => update('requestedQuantity', e.target.value)}
            hint="Number of units required."
          />

          <div className="field-full">
            <TextInput
              label="Material Specification / Description"
              id="description"
              value={form.description}
              onChange={(e) => update('description', e.target.value)}
              hint="For example: OPC 42.5N, 50 kg bag, conforming to SLS specification."
            />
          </div>
        </div>

        <div className="form-actions">
          <Button variant="secondary" onClick={onCancel} disabled={submitting}>
            Cancel
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Submitting…' : 'Submit Request'}
          </Button>
        </div>
      </Card>
    </form>
  )
}

// --------------------------------------------------------------- Review
// STEP 2 of the material-request journey: the Procurement Manager opens a
// pending request, reads what the site asked for, and records Approve /
// Reject / Request Revision. Approving flips the request to Approved, which
// is what unlocks procurement (quotations → analysis → purchase order).

function ReviewRequest({ request, canApprove, isSiteUser, isProcurementReader, onBack }) {
  const [detail, setDetail] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [reloadToken, setReloadToken] = useState(0)
  const [comment, setComment] = useState('')
  const [actionError, setActionError] = useState(null)
  const [deciding, setDeciding] = useState(false)
  const [outcome, setOutcome] = useState(null)
  // Step 3 — RequestAnalysisAgent. Kept separate from `actionError`/`deciding`
  // because analysis is advisory: a failure here must never be confused with a
  // failed approval, and it must not disable the Approve/Reject buttons.
  const [analysis, setAnalysis] = useState(null)
  const [analyzing, setAnalyzing] = useState(false)
  const [analysisError, setAnalysisError] = useState(null)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    qualityApi
      .getMaterialRequest(request.id)
      .then((data) => { if (!cancelled) setDetail(data) })
      .catch((err) => { if (!cancelled) setError(err.message) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [request.id, reloadToken])

  const status = detail?.status ?? request.status
  const decidable = canApprove && isMaterialRequestDecidable(status)

  async function decide(decision) {
    // The backend hard-blocks Rejected without comments; mirror that (and the
    // revision convention from ProcurementApprovalPanel) in the UI so the
    // manager never gets a surprise 400.
    if (decision !== 'Approved' && !comment.trim()) {
      setActionError(
        decision === 'Rejected'
          ? 'A comment is required when rejecting a request.'
          : 'Add a comment explaining what needs to be revised.'
      )
      return
    }
    setActionError(null)
    setDeciding(true)
    try {
      await qualityApi.decideMaterialRequest(request.id, decision, comment.trim() || null)
      setOutcome(decision)
    } catch (err) {
      setActionError(err.message)
    } finally {
      setDeciding(false)
    }
  }

  // Step 3: ask the RequestAnalysisAgent to review this request and surface the
  // flags it returns (e.g. HIGH_URGENCY, LARGE_QUANTITY_ORDER). The result is
  // advisory only — it never changes the request status and never records a
  // decision; the manager still approves by hand.
  async function runAnalysis() {
    setAnalyzing(true)
    setAnalysisError(null)
    try {
      const result = await qualityApi.analyzeRequest(request.id)
      // The backend echoes the id it analysed. Ignore a response for a
      // different request rather than showing another request's flags.
      if (result && result.requestId != null && result.requestId !== request.id) {
        setAnalysisError('The analysis response did not match this request. Please try again.')
        return
      }
      setAnalysis(result)
    } catch (err) {
      setAnalysisError(err.message)
    } finally {
      setAnalyzing(false)
    }
  }

  if (outcome) {
    const copy = {
      Approved: {
        title: `Request #${request.id} approved`,
        message: 'MR is now Approved — procurement can begin (quotations, analysis, then a purchase order).',
      },
      Rejected: {
        title: `Request #${request.id} rejected`,
        message: 'The request was rejected. The site team can revise and resubmit it.',
      },
      RevisionRequested: {
        title: `Revision requested on #${request.id}`,
        message: 'The request was sent back to the site team with your comments.',
      },
    }[outcome]
    return (
      <div className="stack">
        <PageHeader title={`Material Request #${request.id}`} description="Decision recorded." />
        <Card>
          <EmptyState title={copy.title} message={copy.message} />
          <div className="form-actions">
            <Button onClick={onBack}>Back to list</Button>
          </div>
        </Card>
      </div>
    )
  }


  return (
    <div className="stack">
      <PageHeader
        title={`Material Request #${request.id}`}
        description={
          canApprove
            ? 'Review the request, then Approve, Reject, or Request Revision.'
            : 'Request details.'
        }
      />

      {loading && <LoadingState />}
      {error && <ErrorState message={error} onRetry={() => setReloadToken((t) => t + 1)} />}

      {!loading && !error && detail && (
        <Card>
          <div className="detail-row">
            <span className="detail-row__label">Status</span>
            <span className="detail-row__value">
              <StatusBadge status={materialRequestTone(status)}>{status}</StatusBadge>
            </span>
          </div>
          <div className="detail-row">
            <span className="detail-row__label">Project</span>
            <span className="detail-row__value">{detail.projectName || `Project #${detail.projectId}`}</span>
          </div>
          <div className="detail-row">
            <span className="detail-row__label">Priority</span>
            <span className="detail-row__value">{detail.priority}</span>
          </div>
          <div className="detail-row">
            <span className="detail-row__label">Required date</span>
            <span className="detail-row__value">{detail.requiredDate}</span>
          </div>
          <div className="detail-row">
            <span className="detail-row__label">Reason</span>
            <span className="detail-row__value">{detail.reason || '—'}</span>
          </div>
          <div className="detail-row">
            <span className="detail-row__label">Items</span>
            <span className="detail-row__value">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Material</th>
                    <th>Quantity</th>
                    <th>Unit</th>
                    <th>Specification</th>
                  </tr>
                </thead>
                <tbody>
                  {(detail.items ?? []).map((item) => (
                    <tr key={item.id}>
                      <td>{item.materialName}</td>
                      <td>{item.requestedQuantity}</td>
                      <td>{item.unit}</td>
                      <td>{item.description || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </span>
          </div>
        </Card>
      )}


      {!loading && !error && detail && canApprove && (
        <Card
          title="Request Analysis"
          subtitle="Runs the RequestAnalysisAgent over this request to surface planning risks. Advisory only — it does not approve or change the request."
        >
          <div className="form-actions">
            <Button variant="secondary" onClick={runAnalysis} disabled={analyzing}>
              {analyzing ? 'Analyzing…' : analysis ? 'Re-run Analysis' : 'Analyze Request'}
            </Button>
          </div>

          {analysisError && (
            <div style={{ marginTop: 'var(--bw-space-4, 1rem)' }}>
              <ErrorState message={analysisError} />
            </div>
          )}

          {analysis && (
            <div style={{ marginTop: 'var(--bw-space-4, 1rem)' }}>
              <div className="detail-row">
                <span className="detail-row__label">Agent</span>
                <span className="detail-row__value">RequestAnalysisAgent</span>
              </div>
              <div className="detail-row">
                <span className="detail-row__label">Status</span>
                <span className="detail-row__value">{analysis.status}</span>
              </div>
              <div className="detail-row">
                <span className="detail-row__label">Flags</span>
                <span className="detail-row__value">
                  {(analysis.flags ?? []).length === 0 ? (
                    'No flags raised.'
                  ) : (
                    <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: '0.5rem' }}>
                      {analysis.flags.map((flag) => (
                        <li key={flag} style={{ display: 'grid', gap: '0.25rem' }}>
                          <StatusBadge status={requestAnalysisFlagTone(flag)}>
                            {flag}
                          </StatusBadge>
                          <span style={{ fontSize: '0.85rem', opacity: 0.8 }}>
                            {requestAnalysisFlagLabel(flag)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  )}
                </span>
              </div>
            </div>
          )}
        </Card>
      )}

      {!loading && !error && detail && (
        <Card
          title="Decision"
          subtitle={
            decidable
              ? 'Approve to start procurement, or send it back with a comment.'
              : isSiteUser || isProcurementReader
                // Site roles and the Procurement Officer have read-only
                // visibility. Saying "only managers can decide" here would read
                // as if the request were broken; it simply is not theirs to
                // decide. The Officer's next step is RFQ / quotation instead.
                ? isProcurementReader
                  ? 'Read-only. A Procurement Manager or Site Manager decides. Your next step is to raise an RFQ or record quotations for an approved request.'
                  : 'Read-only. A Procurement Manager or Site Manager approves, rejects, or requests a revision.'
                : 'Only Procurement Managers, Site Managers and Administrators can decide, and only while the request is awaiting approval.'
          }
        >
          {actionError && <ErrorState message={actionError} />}
          {decidable && (
            <TextInput
              label="Comment"
              name="comment"
              multiline
              placeholder="Optional for Approve; required for Reject and Request Revision"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
            />
          )}
          <div className="form-actions">
            <Button variant="secondary" onClick={onBack} disabled={deciding}>
              Back to list
            </Button>
            {decidable && (
              <>
                <Button variant="primary" onClick={() => decide('Approved')} disabled={deciding}>
                  {deciding ? 'Saving…' : 'Approve'}
                </Button>
                <Button variant="danger" onClick={() => decide('Rejected')} disabled={deciding}>
                  Reject
                </Button>
                <Button variant="secondary" onClick={() => decide('RevisionRequested')} disabled={deciding}>
                  Request Revision
                </Button>
              </>
            )}
          </div>
        </Card>
      )}
    </div>
  )
}


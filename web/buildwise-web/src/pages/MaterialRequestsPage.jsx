import { useEffect, useState } from 'react'
import {
  Card,
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeader,
  StatusBadge,
  TextInput,
  SelectInput,
} from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'
import './common/common.css'

const STATUS_TONE = {
  Draft: 'neutral',
  Submitted: 'info',
  UnderReview: 'info',
  PendingApproval: 'warning',
  RfqInProgress: 'info',
  AwaitingProcurementApproval: 'warning',
  Approved: 'success',
  Ordered: 'info',
  Completed: 'success',
  Cancelled: 'neutral',
  Rejected: 'danger',
}

export default function MaterialRequestsPage() {
  const { hasRole } = useAuth()
  const [mode, setMode] = useState('list')
  const [requests, setRequests] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  useEffect(() => {
    loadRequests()
  }, []) // Auth session is stable for the lifetime of this authenticated shell.

  async function loadRequests() {
    setLoading(true)
    setError(null)
    try {
      const result = (hasRole('SiteEngineer') || hasRole('SiteOfficer'))
        ? await qualityApi.listMyMaterialRequests()
        : await qualityApi.listMaterialRequests()
      setRequests(result)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  if (mode === 'list') {
    return (
      <div className="stack">
        <div className="toolbar">
          <PageHeader
            title="Material Requests"
            description="Component 1 — site engineers submit material requests for approval."
          />
          {(hasRole('SiteEngineer') || hasRole('SiteOfficer')) ? (
            <button type="button" className="primary-button" onClick={() => setMode('create')}>
              + Create Request
            </button>
          ) : null}
        </div>

        {loading && <LoadingState />}
        {error && <ErrorState message={error} onRetry={loadRequests} />}
        {!loading && !error && (
          <Card>
            {requests.length === 0 ? (
              <EmptyState title="No material requests" message="Submitted requests will appear here." />
            ) : (
              <table className="data-table">
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Project</th>
                    <th>Required Date</th>
                    <th>Items</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {requests.map((r) => (
                    <tr key={r.id}>
                      <td>{r.id}</td>
                      <td>{r.projectName || `Project #${r.projectId}`}</td>
                      <td>{r.requiredDate}</td>
                      <td>{r.itemCount}</td>
                      <td>
                        <StatusBadge tone={STATUS_TONE[r.status] ?? 'neutral'}>
                          {r.status}
                        </StatusBadge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>
        )}
      </div>
    )
  }

    return <CreateRequestForm onCancel={() => setMode('list')} />
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
        <button type="button" className="text-button" onClick={onCancel}>
          ← Back to list
        </button>
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
          <button type="button" className="text-button" onClick={onCancel} disabled={submitting}>
            Cancel
          </button>
          <button type="submit" className="primary-button" disabled={submitting}>
            {submitting ? 'Submitting…' : 'Submit Request'}
          </button>
        </div>
      </Card>
    </form>
  )
}

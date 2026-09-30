import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge, TextInput } from '../../components/shared'
import { qualityApi } from './services/qualityApi'
import QualityRiskPanel from './QualityRiskPanel'
import './quality.css'

const canManageQuality = (roles) => roles.some((role) => ['QualityInspector', 'Administrator'].includes(role))
const show = (value) => value ?? 'Not recorded'
const date = (value) => value ? new Date(value).toLocaleString() : 'Not recorded'
const Badge = ({ value }) => <StatusBadge status={value === 'Completed' || value === 'Closed' || value === 'Resolved' ? 'success' : 'neutral'}>{show(value)}</StatusBadge>

// Ignore obsolete reads after navigation, refresh, or unmount.
function useRecord(load, id) {
  const [state, setState] = useState({ loading: true })
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    let active = true
    setState({ loading: true })
    load(id).then((data) => active && setState({ data }), (error) => active && setState({ error: error.message }))
    return () => { active = false }
  }, [load, id, revision])
  return { ...state, refresh: () => setRevision((value) => value + 1) }
}

function ReadState({ state, children, empty }) {
  if (state.loading) return <LoadingState message="Loading quality records..." />
  if (state.error) return <ErrorState message={state.error} onRetry={state.refresh} />
  if (Array.isArray(state.data) && !state.data.length) return <EmptyState title={empty} message="Refresh after a record has been saved in BuildWise." />
  return children(state.data)
}

export default function QualityApp({ section }) {
  const { roles } = useAuth()
  if (!canManageQuality(roles)) return <ErrorState title="Access restricted" message="Quality management requires the QualityInspector or Administrator role." />
  return <QualityWorkspace key={section} section={section} />
}

function QualityWorkspace({ section, routeView, onNavigate }) {
  const [localView, setLocalView] = useState({ kind: section === 'Non-Conformances' ? 'ncrList' : 'inspectionList' })
  const view = routeView || localView
  const setView = onNavigate || setLocalView
  const openInspection = (id) => setView({ kind: 'inspection', id })
  const openNcr = (id) => setView({ kind: 'ncr', id })
  const back = () => setView({ kind: section === 'Non-Conformances' ? 'ncrList' : 'inspectionList' })
  return <div className="stack quality-workspace">
    {view.kind !== 'inspectionList' && view.kind !== 'ncrList' && <Button variant="secondary" onClick={back}>Back to {section}</Button>}
    {view.kind === 'inspectionList' && <InspectionSection onOpen={openInspection} onBeginInspection={(delivery) => setView({ kind: 'startInspection', delivery })} />}
    {view.kind === 'inspection' && <InspectionDetail key={view.id} id={view.id} onComplete={onNavigate ? (inspection) => setView({ kind: 'completeInspection', inspection }) : undefined} onCreate={(inspection, item) => setView({ kind: 'create', inspection, item })} />}
    {view.kind === 'create' && <div className="workflow-columns ncr-create"><div className="stack"><NcrList onOpen={openNcr} onInspections={() => setView({ kind: 'inspectionList' })} /></div><CreateNcr inspection={view.inspection} item={view.item} onSaved={openNcr} onCancel={() => openInspection(view.inspection.id)} /></div>}
    {view.kind === 'ncrList' && <NcrList onOpen={openNcr} onInspections={() => setView({ kind: 'inspectionList' })} />}
    {view.kind === 'ncr' && <NcrDetail key={view.id} id={view.id} onInspection={openInspection} />}
    {view.kind === 'startInspection' && (
      <StartInspectionForm
        delivery={view.delivery}
        onSuccess={(inspection) => setView({ kind: 'completeInspection', inspection })}
        onCancel={() => setView({ kind: 'inspectionList' })}
      />
    )}
    {view.kind === 'completeInspection' && view.inspection.status === 'UnderInspection' && (
      <CompleteInspectionForm
        inspection={view.inspection}
        onSuccess={(id) => openInspection(id)}
        onCancel={() => setView({ kind: 'inspectionList' })}
      />
    )}
    {view.kind === 'completeInspection' && view.inspection.status !== 'UnderInspection' && <InspectionDetail id={view.inspection.id} onCreate={(inspection, item) => setView({ kind: 'create', inspection, item })} />}
  </div>
}

// URL-driven adapter reuses the forms above; business validation stays in place.
export function RoutedQualityApp({ kind }) {
  const params = useParams()
  const navigate = useNavigate()
  const section = kind.startsWith('ncr') ? 'Non-Conformances' : 'Quality Inspections'
  const onNavigate = (view) => {
    const paths = {
      inspectionList: '/quality-inspections',
      ncrList: '/non-conformances',
      inspection: `/quality-inspections/${view.id}`,
      ncr: `/non-conformances/${view.id}`,
      startInspection: `/quality-inspections/new/${view.delivery?.deliveryId}`,
      completeInspection: `/quality-inspections/${view.inspection?.id}/complete`,
      create: `/quality-inspections/${view.inspection?.id}/non-conformances/new/${view.item?.id}`,
    }
    navigate(paths[view.kind])
  }
  const view = { kind, id: params.id }
  if (['startInspection', 'completeInspection', 'create'].includes(kind)) {
    return <QualityFormRoute key={`${kind}:${params.id}:${params.deliveryId}:${params.itemId}`} kind={kind} params={params} section={section} onNavigate={onNavigate} />
  }
  return <QualityWorkspace section={section} routeView={view} onNavigate={onNavigate} />
}

function QualityFormRoute({ kind, params, section, onNavigate }) {
  const load = useCallback(async () => {
    if (kind === 'startInspection') {
      const deliveries = await qualityApi.pendingDeliveries()
      const delivery = deliveries.find((item) => String(item.deliveryId) === params.deliveryId)
      if (!delivery) throw new Error('This delivery is no longer pending inspection.')
      return { kind, delivery }
    }
    const inspection = await qualityApi.getInspection(params.id)
    if (kind === 'completeInspection') return inspection.status === 'UnderInspection'
      ? { kind, inspection }
      : { kind: 'inspection', id: inspection.id }
    const item = inspection.items.find((item) => String(item.id) === params.itemId)
    if (!item) throw new Error('Inspection item not found.')
    return { kind, inspection, item }
  }, [kind, params.id, params.deliveryId, params.itemId])
  const state = useRecord(load)
  return <ReadState state={state}>{(view) => <QualityWorkspace section={section} routeView={view} onNavigate={onNavigate} />}</ReadState>
}

// ─────────────────────────────────────────────────────────────────────────────
// Inspection section: pending deliveries + history
// ─────────────────────────────────────────────────────────────────────────────

function InspectionSection({ onOpen, onBeginInspection }) {
  const [revision, setRevision] = useState(0)
  const refresh = () => setRevision((v) => v + 1)
  return <>
    <PageHeader
      title="Quality Inspections"
      description="Inspection history recorded through the shared BuildWise API."
      actions={<Button variant="secondary" onClick={refresh}>Refresh</Button>}
    />
    <PendingDeliveries key={`pending-${revision}`} onBeginInspection={onBeginInspection} />
    <InspectionHistory key={`history-${revision}`} onOpen={onOpen} />
  </>
}

// ─────────────────────────────────────────────────────────────────────────────
// Pending Inspections panel — calls GET /api/inspections/pending-deliveries
// ─────────────────────────────────────────────────────────────────────────────

function PendingDeliveries({ onBeginInspection }) {
  const load = useCallback(() => qualityApi.pendingDeliveries(), [])
  const state = useRecord(load)
  if (state.loading) return <LoadingState message="Loading pending inspections..." />
  if (state.error) return <ErrorState message={state.error} onRetry={state.refresh} />
  const rows = state.data ?? []
  if (!rows.length) return (
    <Card title="Pending Inspections">
      <EmptyState title="No pending inspections" message="Deliveries with Received or DiscrepancyReported status and received items will appear here." />
    </Card>
  )
  return (
    <Card title="Pending Inspections">
      <div className="table-wrap">
        <table className="data-table">
          <thead><tr>
            <th>Delivery Reference</th>
            <th>Status</th>
            <th>Items (received)</th>
            <th>Total Received Qty</th>
            <th>Total Damaged Qty</th>
            <th>Action</th>
          </tr></thead>
          <tbody>{rows.map((d) => {
            const totalReceived = d.items.reduce((sum, i) => sum + i.receivedQuantity, 0)
            const totalDamaged = d.items.reduce((sum, i) => sum + i.damagedQuantity, 0)
            return (
              <tr key={d.deliveryId}>
                <td><strong>{d.deliveryReference || `Delivery #${d.deliveryId}`}</strong></td>
                <td><Badge value={d.status} /></td>
                <td>{d.items.length}</td>
                <td>{totalReceived}</td>
                <td>{totalDamaged > 0 ? <span style={{ color: '#c0392b' }}>{totalDamaged}</span> : totalDamaged}</td>
                <td>
                  <button
                    className="table-action"
                    onClick={() => onBeginInspection(d)}
                  >
                    Start Inspection
                  </button>
                </td>
              </tr>
            )
          })}</tbody>
        </table>
      </div>
    </Card>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Start Inspection form — calls POST /api/inspections
// ─────────────────────────────────────────────────────────────────────────────

function StartInspectionForm({ delivery, onSuccess, onCancel }) {
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const locked = useRef(false)
  const active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])

  async function submit(e) {
    e.preventDefault()
    if (locked.current) return
    locked.current = true
    setBusy(true); setError('')
    try {
      const inspection = await qualityApi.startInspection({
        deliveryId: delivery.deliveryId,
        notes: notes.trim() || null
      })
      if (active.current) onSuccess(inspection)
    } catch (err) {
      if (active.current) setError(err.message)
    } finally {
      locked.current = false
      if (active.current) setBusy(false)
    }
  }

  return (
    <Card title={`Start Inspection — ${delivery.deliveryReference || `Delivery #${delivery.deliveryId}`}`}>
      <p>
        Delivery <strong>{delivery.deliveryReference || `#${delivery.deliveryId}`}</strong> has{' '}
        {delivery.items.length} item(s) ready for inspection.
        The backend will record you as the inspector from your session token.
      </p>
      <form className="stack" onSubmit={submit}>
        <TextInput
          name="notes"
          label="Initial notes (optional)"
          multiline
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          disabled={busy}
        />
        {error && <ErrorState message={error} />}
        <div className="quality-actions">
          <Button type="submit" disabled={busy}>{busy ? 'Starting…' : 'Start Inspection'}</Button>
          <Button variant="secondary" disabled={busy} onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Complete Inspection form — calls POST /api/inspections/{id}/complete
// Backend validation rules are preserved:
//   • Every positive-received delivery item must be included
//   • accepted + rejected ≤ received
//   • Accepted → zero rejected; Rejected → zero accepted; PartiallyAccepted → both > 0
// ─────────────────────────────────────────────────────────────────────────────

const DECISIONS = ['Accepted', 'PartiallyAccepted', 'Rejected']

function CompleteInspectionForm({ inspection, onSuccess, onCancel }) {
  const deliveryItems = inspection.deliveryItems ?? []

  const initItems = () =>
    deliveryItems.map((di) => ({
      deliveryItemId: di.deliveryItemId,
      receivedQuantity: di.receivedQuantity,
      damagedQuantity: di.damagedQuantity,
      condition: '',
      acceptedQuantity: '',
      rejectedQuantity: '',
      remarks: ''
    }))

  const [items, setItems] = useState(initItems)
  const [decision, setDecision] = useState('')
  const [notes, setNotes] = useState(inspection.notes ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const locked = useRef(false)
  const active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])

  function setItemField(idx, field, value) {
    setItems((prev) => prev.map((it, i) => i === idx ? { ...it, [field]: value } : it))
  }

  function clientValidate() {
    if (!decision) return 'Select an overall decision.'
    for (const it of items) {
      const acc = parseFloat(it.acceptedQuantity) || 0
      const rej = parseFloat(it.rejectedQuantity) || 0
      if (acc < 0 || rej < 0) return 'Quantities cannot be negative.'
      if (acc + rej > it.receivedQuantity)
        return `Item #${it.deliveryItemId}: accepted + rejected (${acc + rej}) exceeds received (${it.receivedQuantity}).`
    }
    const totalAcc = items.reduce((s, it) => s + (parseFloat(it.acceptedQuantity) || 0), 0)
    const totalRej = items.reduce((s, it) => s + (parseFloat(it.rejectedQuantity) || 0), 0)
    if (decision === 'Accepted' && totalRej > 0) return 'Accepted requires zero rejected quantity.'
    if (decision === 'Accepted' && totalAcc === 0) return 'Accepted requires a positive accepted quantity.'
    if (decision === 'Rejected' && totalAcc > 0) return 'Rejected requires zero accepted quantity.'
    if (decision === 'Rejected' && totalRej === 0) return 'Rejected requires a positive rejected quantity.'
    if (decision === 'PartiallyAccepted' && (totalAcc === 0 || totalRej === 0))
      return 'PartiallyAccepted requires both accepted and rejected quantities.'
    return null
  }

  async function submit(e) {
    e.preventDefault()
    const clientError = clientValidate()
    if (clientError) { setError(clientError); return }
    if (locked.current) return
    locked.current = true
    setBusy(true); setError('')
    try {
      const body = {
        overallDecision: decision,
        notes: notes.trim() || null,
        items: items.map((it) => ({
          deliveryItemId: it.deliveryItemId,
          condition: it.condition.trim() || null,
          acceptedQuantity: parseFloat(it.acceptedQuantity) || 0,
          rejectedQuantity: parseFloat(it.rejectedQuantity) || 0,
          remarks: it.remarks.trim() || null
        }))
      }
      const completed = await qualityApi.completeInspection(inspection.id, body)
      if (active.current) onSuccess(completed.id)
    } catch (err) {
      if (active.current) setError(err.message)
    } finally {
      locked.current = false
      if (active.current) setBusy(false)
    }
  }

  return (
    <div className="workflow-columns"><div className="stack">
      <Card title={`Complete Inspection #${inspection.id}`}><div className="context-ribbon"><div><span>Delivery</span><strong>{inspection.deliveryReference || `Delivery #${inspection.deliveryId}`}</strong></div><div><span>Inspector</span><strong>{inspection.inspectorName || 'Current inspector'}</strong></div><div><span>Status</span><Badge value={inspection.status} /></div></div></Card>
      <div className="request-items-header"><h3>Material Items for Assessment</h3><span className="muted">Accepted + Rejected ≤ Received</span></div>
      <form id="complete-inspection-form" className="stack" onSubmit={submit}>
        {/* Per-item form rows */}
        {items.map((it, idx) => (
          <Card key={it.deliveryItemId}>
            <div className="inspection-item-heading"><strong>Delivery Item #{it.deliveryItemId}</strong><span className="muted">Received: {it.receivedQuantity} · Damaged: {it.damagedQuantity ?? 'Not recorded'}</span></div>
            <div className="inspection-item-fields">
              <TextInput
                name={`condition-${idx}`}
                label="Condition"
                value={it.condition}
                onChange={(e) => setItemField(idx, 'condition', e.target.value)}
                disabled={busy}
              />
              <TextInput
                name={`accepted-${idx}`}
                label="Accepted quantity"
                type="number"
                min="0"
                step="0.01"
                value={it.acceptedQuantity}
                onChange={(e) => setItemField(idx, 'acceptedQuantity', e.target.value)}
                disabled={busy}
              />
              <TextInput
                name={`rejected-${idx}`}
                label="Rejected quantity"
                type="number"
                min="0"
                step="0.01"
                value={it.rejectedQuantity}
                onChange={(e) => setItemField(idx, 'rejectedQuantity', e.target.value)}
                disabled={busy}
              />
              <TextInput name={`remarks-${idx}`} label="Remarks" multiline value={it.remarks} onChange={(e) => setItemField(idx, 'remarks', e.target.value)} disabled={busy} />
            </div>
          </Card>
        ))}

        {/* Overall decision and notes */}
        <Card title="Overall Inspection Decision"><div className="form-grid"><SelectInput
          name="decision"
          label="Overall decision"
          value={decision}
          onChange={(e) => setDecision(e.target.value)}
          disabled={busy}
          options={[
            { value: '', label: '— Select decision —' },
            ...DECISIONS.map((d) => ({ value: d, label: d }))
          ]}
        />
        <TextInput
          name="complete-notes"
          label="Inspection notes"
          multiline
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          disabled={busy}
        />
        </div></Card>

        {error && <ErrorState message={error} />}

      </form>
    </div><aside className="workflow-summary"><Card title="Inspection Summary"><dl className="summary-facts"><dt>Total received</dt><dd>{items.reduce((s, i) => s + i.receivedQuantity, 0)}</dd><dt>Total accepted</dt><dd>{items.reduce((s, i) => s + Number(i.acceptedQuantity || 0), 0)}</dd><dt>Total rejected</dt><dd>{items.reduce((s, i) => s + Number(i.rejectedQuantity || 0), 0)}</dd><dt>Overall decision</dt><dd>{decision || 'Not selected'}</dd></dl><p className="muted">Physical inspection is a human decision. AI risk advice does not change these quantities.</p><div className="stack"><Button type="submit" form="complete-inspection-form" disabled={busy}>{busy ? 'Completing…' : 'Complete Inspection'}</Button><Button variant="secondary" disabled={busy} onClick={onCancel}>Cancel</Button></div></Card></aside></div>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Inspection History list
// ─────────────────────────────────────────────────────────────────────────────

function InspectionHistory({ onOpen }) {
  const state = useRecord(qualityApi.listInspections)
  return <>
    <Card><ReadState state={state} empty="No inspections yet">{(rows) => <div className="table-wrap"><table className="data-table">
      <thead><tr><th>Inspection</th><th>Delivery</th><th>Inspector</th><th>Date</th><th>Status</th><th>Decision</th></tr></thead>
      <tbody>{rows.map((row) => <tr key={row.id}>
        <td><button className="table-action" onClick={() => onOpen(row.id)}>Inspection #{row.id}</button></td>
        <td>{row.deliveryReference || `Delivery #${row.deliveryId}`}</td><td>{row.inspectorName || `User #${row.inspectorUserId}`}</td>
        <td>{date(row.inspectionDate)}</td><td><Badge value={row.status} /></td><td>{show(row.overallDecision)}</td>
      </tr>)}</tbody>
    </table></div>}</ReadState></Card>
  </>
}

// ─────────────────────────────────────────────────────────────────────────────
// Inspection Detail (view + NCR creation trigger)
// ─────────────────────────────────────────────────────────────────────────────

function InspectionDetail({ id, onCreate, onComplete }) {
  const state = useRecord(qualityApi.getInspection, id)
  return <><PageHeader title={`Inspection #${id}`} actions={<Button variant="secondary" onClick={state.refresh}>Refresh</Button>} />
    <ReadState state={state}>{(inspection) => <>
      {onComplete && inspection.status === 'UnderInspection' && <Button onClick={() => onComplete(inspection)}>Complete inspection</Button>}
      <Card title="Inspection details"><dl className="quality-facts">
        <dt>Delivery</dt><dd>{inspection.deliveryReference || `Delivery #${inspection.deliveryId}`} (#{inspection.deliveryId})</dd>
        <dt>Inspector</dt><dd>{inspection.inspectorName || `User #${inspection.inspectorUserId}`}</dd>
        <dt>Inspection date</dt><dd>{date(inspection.inspectionDate)}</dd><dt>Status</dt><dd><Badge value={inspection.status} /></dd>
        <dt>Overall decision</dt><dd>{show(inspection.overallDecision)}</dd><dt>Notes</dt><dd>{show(inspection.notes)}</dd>
      </dl></Card>
      <Card title="Inspection items">{!inspection.items.length ? <EmptyState title="No inspection items recorded" message="Items appear when the inspection is completed." /> : <div className="table-wrap"><table className="data-table">
        <thead><tr><th>Item / delivery item</th><th>Condition</th><th>Received</th><th>Accepted</th><th>Rejected</th><th>Remarks</th><th>Action</th></tr></thead>
        <tbody>{inspection.items.map((item) => <tr key={item.id}>
          <td>#{item.id} / #{item.deliveryItemId}</td><td>{show(item.condition)}</td>
          <td>{show(inspection.deliveryItems?.find((di) => di.deliveryItemId === item.deliveryItemId)?.receivedQuantity)}</td>
          <td>{item.acceptedQuantity}</td><td>{item.rejectedQuantity}</td><td>{show(item.remarks)}</td>
          <td>{inspection.status === 'Completed' && item.rejectedQuantity > 0 && <button className="table-action" onClick={() => onCreate(inspection, item)}>Create NCR for item #{item.id}</button>}</td>
        </tr>)}</tbody></table></div>}</Card>
      <QualityRiskPanel key={inspection.id} inspection={inspection} />
    </>}</ReadState>
  </>
}

// ─────────────────────────────────────────────────────────────────────────────
// Create NCR
// ─────────────────────────────────────────────────────────────────────────────

function CreateNcr({ inspection, item, onSaved, onCancel }) {
  const [issue, setIssue] = useState('')
  const [severity, setSeverity] = useState('Medium')
  const [action, setAction] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const locked = useRef(false)
  const active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  async function submit(event) {
    event.preventDefault()
    if (!issue.trim()) { setError('Enter an issue description.'); return }
    if (locked.current) return
    locked.current = true
    setBusy(true); setError('')
    try {
      const record = await qualityApi.createNcr({ inspectionItemId: item.id, issueDescription: issue.trim(), severity, correctiveAction: action.trim() || null })
      if (active.current) onSaved(record.id)
    } catch (err) { if (active.current) setError(err.message) }
    finally { locked.current = false; if (active.current) setBusy(false) }
  }
  return <Card title="Create non-conformance" subtitle={`Inspection #${inspection.id}, item #${item.id}: ${item.rejectedQuantity} rejected.`}>
    <p>You are recording this NCR as the responsible human reviewer.</p>
    <form className="stack" onSubmit={submit}>
      <TextInput name="issue" label="Issue description" multiline required value={issue} onChange={(e) => setIssue(e.target.value)} disabled={busy} />
      <SelectInput name="severity" label="Severity" value={severity} onChange={(e) => setSeverity(e.target.value)} disabled={busy} options={['Low', 'Medium', 'High', 'Critical'].map((value) => ({ value, label: value }))} />
      <TextInput name="initial-action" label="Corrective action (optional)" multiline value={action} onChange={(e) => setAction(e.target.value)} disabled={busy} />
      {error && <ErrorState message={error} />}
      <div className="quality-actions"><Button type="submit" disabled={busy}>{busy ? 'Saving...' : 'Create NCR'}</Button><Button variant="secondary" disabled={busy} onClick={onCancel}>Cancel</Button></div>
    </form>
  </Card>
}

// ─────────────────────────────────────────────────────────────────────────────
// NCR List
// ─────────────────────────────────────────────────────────────────────────────

function NcrList({ onOpen, onInspections }) {
  const state = useRecord(qualityApi.listNcrs)
  return <><PageHeader title="Non-Conformances" description="Track rejected inspection items through corrective action and closure." actions={<div className="quality-actions"><Button onClick={onInspections}>Choose inspection to create NCR</Button><Button variant="secondary" onClick={state.refresh}>Refresh</Button></div>} />
    <Card><ReadState state={state} empty="No non-conformances yet">{(rows) => <div className="table-wrap"><table className="data-table">
      <thead><tr><th>NCR</th><th>Inspection / item</th><th>Issue</th><th>Severity</th><th>Status</th><th>Created</th></tr></thead>
      <tbody>{rows.map((row) => <tr key={row.id}><td><button className="table-action" onClick={() => onOpen(row.id)}>NCR #{row.id}</button></td><td>#{row.inspectionId} / #{row.inspectionItemId}</td><td>{row.issueDescription}</td><td>{row.severity}</td><td><Badge value={row.status} /></td><td>{date(row.createdAt)}</td></tr>)}</tbody>
    </table></div>}</ReadState></Card>
  </>
}

// ─────────────────────────────────────────────────────────────────────────────
// NCR Detail
// ─────────────────────────────────────────────────────────────────────────────

function NcrDetail({ id, onInspection }) {
  const state = useRecord(qualityApi.getNcr, id)
  return <><PageHeader title={`NCR #${id}`} actions={<Button variant="secondary" onClick={state.refresh}>Refresh</Button>} />
    <ReadState state={state}>{(record) => <NcrEditor key={`${record.id}:${record.updatedAt}`} record={record} onChanged={state.refresh} onInspection={onInspection} />}</ReadState>
  </>
}

function NcrEditor({ record, onChanged, onInspection }) {
  const [action, setAction] = useState(record.correctiveAction || '')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const locked = useRef(false)
  const editable = ['Open', 'CorrectiveActionRequired'].includes(record.status)
  async function mutate(operation) {
    if (locked.current) return
    locked.current = true; setBusy(true); setError('')
    try { await operation(); onChanged() }
    catch (err) { setError(err.message) }
    finally { locked.current = false; setBusy(false) }
  }
  const lifecycle = ['Open', 'CorrectiveActionRequired', 'Resolved', 'Closed']
  return <><Card title="NCR Workflow Lifecycle"><ol className="ncr-steps">{lifecycle.map((status, index) => <li key={status} aria-current={status === record.status ? 'step' : undefined} data-complete={index < lifecycle.indexOf(record.status)}>{index + 1}. {status.replace(/([a-z])([A-Z])/g, '$1 $2')}</li>)}</ol></Card><div className="workflow-columns"><div className="stack"><Card title="Non-conformance details">
    <dl className="quality-facts">
      <dt>Inspection</dt><dd><button className="table-action" onClick={() => onInspection(record.inspectionId)}>Inspection #{record.inspectionId}</button>, item #{record.inspectionItemId}, delivery item #{record.deliveryItemId}</dd>
      <dt>Severity</dt><dd>{record.severity}</dd><dt>Status</dt><dd><Badge value={record.status} /></dd>
      <dt>Condition</dt><dd>{show(record.condition)}</dd><dt>Accepted / rejected</dt><dd>{record.acceptedQuantity} / {record.rejectedQuantity}</dd>
      <dt>Remarks</dt><dd>{show(record.remarks)}</dd>
    </dl>
    </Card><Card title="Issue Description"><p>{record.issueDescription}</p></Card></div><div className="stack"><Card title="Corrective Action Plan">
    <p className="advisory-note">{show(record.correctiveAction)}</p>
    {error && <ErrorState message={error} />}
    {editable && <form className="stack" onSubmit={(event) => { event.preventDefault(); if (!action.trim()) { setError('Enter a corrective action.'); return }; mutate(() => qualityApi.updateCorrectiveAction(record.id, action.trim())) }}>
      <TextInput name="corrective-action" label="Corrective action" multiline required value={action} disabled={busy} onChange={(e) => setAction(e.target.value)} />
      <Button type="submit" disabled={busy}>Save corrective action</Button>
    </form>}
    <div className="quality-actions">
      {editable && record.correctiveAction?.trim() && <Button disabled={busy || action.trim() !== record.correctiveAction.trim()} onClick={() => mutate(() => qualityApi.resolveNcr(record.id))}>Mark resolved</Button>}
      {record.status === 'Resolved' && <Button disabled={busy} onClick={() => mutate(() => qualityApi.closeNcr(record.id))}>Close NCR</Button>}
    </div>
  </Card><Card title="Audit History"><dl className="quality-facts"><dt>Created</dt><dd>{date(record.createdAt)}</dd><dt>Updated</dt><dd>{date(record.updatedAt)}</dd><dt>Resolved</dt><dd>{date(record.resolvedAt)}</dd></dl></Card></div></div></>
}

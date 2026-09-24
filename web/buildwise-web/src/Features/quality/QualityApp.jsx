import { useEffect, useRef, useState } from 'react'
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

function QualityWorkspace({ section }) {
  const [view, setView] = useState({ kind: section === 'Non-Conformances' ? 'ncrList' : 'inspectionList' })
  const openInspection = (id) => setView({ kind: 'inspection', id })
  const openNcr = (id) => setView({ kind: 'ncr', id })
  const back = () => setView({ kind: section === 'Non-Conformances' ? 'ncrList' : 'inspectionList' })
  return <div className="stack quality-workspace">
    {view.kind !== 'inspectionList' && view.kind !== 'ncrList' && <Button variant="secondary" onClick={back}>Back to {section}</Button>}
    {view.kind === 'inspectionList' && <InspectionHistory onOpen={openInspection} />}
    {view.kind === 'inspection' && <InspectionDetail key={view.id} id={view.id} onCreate={(inspection, item) => setView({ kind: 'create', inspection, item })} />}
    {view.kind === 'create' && <CreateNcr inspection={view.inspection} item={view.item} onSaved={openNcr} onCancel={() => openInspection(view.inspection.id)} />}
    {view.kind === 'ncrList' && <NcrList onOpen={openNcr} onInspections={() => setView({ kind: 'inspectionList' })} />}
    {view.kind === 'ncr' && <NcrDetail key={view.id} id={view.id} onInspection={openInspection} />}
  </div>
}

function InspectionHistory({ onOpen }) {
  const state = useRecord(qualityApi.listInspections)
  return <>
    <PageHeader title="Quality Inspections" description="Inspection history recorded through the shared BuildWise API." actions={<Button variant="secondary" onClick={state.refresh}>Refresh</Button>} />
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

function InspectionDetail({ id, onCreate }) {
  const state = useRecord(qualityApi.getInspection, id)
  return <><PageHeader title={`Inspection #${id}`} actions={<Button variant="secondary" onClick={state.refresh}>Refresh</Button>} />
    <ReadState state={state}>{(inspection) => <>
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

function NcrList({ onOpen, onInspections }) {
  const state = useRecord(qualityApi.listNcrs)
  return <><PageHeader title="Non-Conformances" description="Track rejected inspection items through corrective action and closure." actions={<div className="quality-actions"><Button onClick={onInspections}>Choose inspection to create NCR</Button><Button variant="secondary" onClick={state.refresh}>Refresh</Button></div>} />
    <Card><ReadState state={state} empty="No non-conformances yet">{(rows) => <div className="table-wrap"><table className="data-table">
      <thead><tr><th>NCR</th><th>Inspection / item</th><th>Issue</th><th>Severity</th><th>Status</th><th>Created</th></tr></thead>
      <tbody>{rows.map((row) => <tr key={row.id}><td><button className="table-action" onClick={() => onOpen(row.id)}>NCR #{row.id}</button></td><td>#{row.inspectionId} / #{row.inspectionItemId}</td><td>{row.issueDescription}</td><td>{row.severity}</td><td><Badge value={row.status} /></td><td>{date(row.createdAt)}</td></tr>)}</tbody>
    </table></div>}</ReadState></Card>
  </>
}

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
  return <Card title="Non-conformance details">
    <dl className="quality-facts">
      <dt>Inspection</dt><dd><button className="table-action" onClick={() => onInspection(record.inspectionId)}>Inspection #{record.inspectionId}</button>, item #{record.inspectionItemId}, delivery item #{record.deliveryItemId}</dd>
      <dt>Issue</dt><dd>{record.issueDescription}</dd><dt>Severity</dt><dd>{record.severity}</dd><dt>Status</dt><dd><Badge value={record.status} /></dd>
      <dt>Condition</dt><dd>{show(record.condition)}</dd><dt>Accepted / rejected</dt><dd>{record.acceptedQuantity} / {record.rejectedQuantity}</dd>
      <dt>Remarks</dt><dd>{show(record.remarks)}</dd><dt>Corrective action</dt><dd>{show(record.correctiveAction)}</dd>
      <dt>Created</dt><dd>{date(record.createdAt)}</dd><dt>Updated</dt><dd>{date(record.updatedAt)}</dd><dt>Resolved</dt><dd>{date(record.resolvedAt)}</dd>
    </dl>
    {error && <ErrorState message={error} />}
    {editable && <form className="stack" onSubmit={(event) => { event.preventDefault(); if (!action.trim()) { setError('Enter a corrective action.'); return }; mutate(() => qualityApi.updateCorrectiveAction(record.id, action.trim())) }}>
      <TextInput name="corrective-action" label="Corrective action" multiline required value={action} disabled={busy} onChange={(e) => setAction(e.target.value)} />
      <Button type="submit" disabled={busy}>Save corrective action</Button>
    </form>}
    <div className="quality-actions">
      {editable && record.correctiveAction?.trim() && <Button disabled={busy || action.trim() !== record.correctiveAction.trim()} onClick={() => mutate(() => qualityApi.resolveNcr(record.id))}>Mark resolved</Button>}
      {record.status === 'Resolved' && <Button disabled={busy} onClick={() => mutate(() => qualityApi.closeNcr(record.id))}>Close NCR</Button>}
    </div>
  </Card>
}

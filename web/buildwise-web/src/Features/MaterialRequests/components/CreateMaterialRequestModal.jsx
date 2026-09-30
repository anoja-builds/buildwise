import { useEffect, useRef, useState } from 'react'
import { materialRequestService } from '../services/materialRequestService'
import { Button, Card, ErrorState, LoadingState, PageHeader, TextInput } from '../../../components/shared'
const blankItem = () => ({ materialId: '', quantity: '', notes: '' })
export default function CreateMaterialRequestModal({ onClose, onSuccess }) {
  const [options, setOptions] = useState(null)
  const [optionsError, setOptionsError] = useState('')
  const [revision, setRevision] = useState(0)
  const [projectId, setProjectId] = useState('')
  const [requiredDate, setRequiredDate] = useState('')
  const [reason, setReason] = useState('')
  const [items, setItems] = useState([blankItem()])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const locked = useRef(false)
  useEffect(() => {
    let active = true
    setOptions(null); setOptionsError('')
    materialRequestService.getOptions().then(data => { if (active) setOptions(data) }, err => { if (active) setOptionsError(err.message) })
    return () => { active = false }
  }, [revision])
  const updateItem = (index, field, value) => setItems(current => current.map((item, i) => i === index ? { ...item, [field]: value } : item))
  const available = options?.projects?.length > 0 && options?.materials?.length > 0
  async function submit(event) {
    event.preventDefault()
    if (locked.current || !available) return
    if (!options.projects.some(p => String(p.id) === projectId) || items.some(i => !options.materials.some(m => String(m.id) === i.materialId) || !(Number(i.quantity) > 0))) {
      setError('Select a project, active materials, and positive quantities.'); return
    }
    locked.current = true; setBusy(true); setError('')
    try {
      // ASP.NET derives requester identity exclusively from the authenticated JWT.
      await materialRequestService.createRequest({ projectId: Number(projectId), requiredDate: new Date(requiredDate).toISOString(), reason: reason.trim(), submitImmediately: event.nativeEvent.submitter?.value !== 'draft', items: items.map(i => ({ materialId: Number(i.materialId), quantity: Number(i.quantity), unit: options.materials.find(m => String(m.id) === i.materialId).unit, notes: i.notes.trim() || null })) })
      onSuccess?.()
    } catch (err) { setError(err.message) }
    finally { locked.current = false; setBusy(false) }
  }
  return <section className="stack request-create" aria-label="Create Material Request">
    <PageHeader title="New Material Request" description="Create and submit a material request for on-site construction materials." />
    {!options && !optionsError && <LoadingState message="Loading projects and materials..." />}
    {optionsError && <ErrorState message={optionsError} onRetry={() => setRevision(v => v + 1)} />}
    {options && !available && <Card title="No request options available"><p>An active project and material are required. Ask an administrator to update the catalogue.</p></Card>}
    {error && <ErrorState message={error} />}
    {available && <Card><form className="stack" onSubmit={submit}>
      <div className="form-grid">
        <label className="field"><span className="field__label">Project *</span><select className="field__control" aria-label="Project" required value={projectId} disabled={busy} onChange={e => setProjectId(e.target.value)}><option value="">Select project...</option>{options.projects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <TextInput label="Required Date" name="requiredDate" type="date" required value={requiredDate} disabled={busy} onChange={e => setRequiredDate(e.target.value)} />
      </div>
      <TextInput label="Reason / Justification" name="reason" multiline required value={reason} disabled={busy} onChange={e => setReason(e.target.value)} />
      <div className="request-items-header"><h3>Requested Materials</h3><Button variant="secondary" disabled={busy} onClick={() => setItems(current => [...current, blankItem()])}>+ Add Material</Button></div>
      {items.map((item, index) => <div className="request-item-row" key={index}>
        <label className="field"><span className="field__label">Material *</span><select className="field__control" aria-label={`Material ${index + 1}`} required disabled={busy} value={item.materialId} onChange={e => updateItem(index, 'materialId', e.target.value)}><option value="">Select material...</option>{options.materials.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select></label>
        <TextInput label="Quantity" name={`quantity-${index}`} type="number" min="0.01" step="0.01" required disabled={busy} value={item.quantity} onChange={e => updateItem(index, 'quantity', e.target.value)} />
        <TextInput label="Unit" name={`unit-${index}`} value={options.materials.find(m => String(m.id) === item.materialId)?.unit || ''} readOnly />
        <TextInput label="Notes" name={`notes-${index}`} disabled={busy} value={item.notes} onChange={e => updateItem(index, 'notes', e.target.value)} />
        <Button variant="secondary" aria-label={`Remove material ${index + 1}`} disabled={busy || items.length === 1} onClick={() => setItems(current => current.filter((_, i) => i !== index))}>×</Button>
      </div>)}
      <div className="form-actions"><Button variant="secondary" onClick={onClose} disabled={busy}>Cancel</Button><Button type="submit" value="draft" variant="secondary" disabled={busy}>Save as Draft</Button><Button type="submit" value="submit" disabled={busy}>{busy ? 'Saving...' : 'Submit for Approval'}</Button></div>
    </form></Card>}
    {!available && <Button variant="secondary" onClick={onClose}>Back to Material Requests</Button>}
  </section>
}

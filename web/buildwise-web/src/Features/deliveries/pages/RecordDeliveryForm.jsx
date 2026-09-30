import { useRef, useState } from 'react'
import { deliveryService } from '../services/deliveryService'
import { Button, Card, StatusBadge, TextInput } from '../../../components/shared'

export default function RecordDeliveryForm({ delivery, onCancel, onSuccess }) {
  const [notes, setNotes] = useState('')
  const [evidenceUrl, setEvidenceUrl] = useState('')
  const [items, setItems] = useState(() => delivery.items.map(item => ({ ...item, outstandingQuantity: item.outstandingQuantity ?? item.orderedQuantity, receivedQuantity: item.outstandingQuantity ?? item.orderedQuantity, damagedQuantity: 0, notes: '' })))
  const [busy, setBusy] = useState(false)
  const [receipt, setReceipt] = useState(null)
  const [error, setError] = useState('')
  const locked = useRef(false)
  const update = (index, field, value) => setItems(current => current.map((item, i) => i === index ? { ...item, [field]: value } : item))
  const damaged = items.reduce((s, i) => s + Number(i.damagedQuantity || 0), 0)
  const discrepancy = items.some(i => Number(i.receivedQuantity) - Number(i.damagedQuantity) < i.outstandingQuantity || Number(i.damagedQuantity) > 0)
  const validation = items.some(i => !Number.isFinite(Number(i.receivedQuantity)) || !Number.isFinite(Number(i.damagedQuantity)) || Number(i.receivedQuantity) < 0 || Number(i.receivedQuantity) > i.outstandingQuantity || Number(i.damagedQuantity) < 0 || Number(i.damagedQuantity) > Number(i.receivedQuantity))
  async function submit(event) {
    event.preventDefault()
    if (locked.current) return
    if (!receipt && validation) { setError('Received quantity must be within outstanding quantity. Damaged quantity must be between zero and received quantity.'); return }
    if (evidenceUrl && !/^https?:\/\//i.test(evidenceUrl)) { setError('Enter an existing HTTP or HTTPS evidence link.'); return }
    locked.current = true; setBusy(true); setError('')
    try {
      if (!receipt) {
        const result = await deliveryService.receiveDelivery(delivery.id, { notes, items: items.map(i => ({ purchaseOrderItemId: i.purchaseOrderItemId, receivedQuantity: Number(i.receivedQuantity), damagedQuantity: Number(i.damagedQuantity), notes: i.notes })) })
        setReceipt(result)
      }
      if (evidenceUrl.trim()) await deliveryService.saveEvidenceLink(delivery.id, evidenceUrl.trim())
      onSuccess()
    } catch (err) { setError(err.message || 'Unable to save. Review the server result before retrying.') }
    finally { locked.current = false; setBusy(false) }
  }
  return <section className="stack receiving-workspace">
    <div className="page-header"><div><h1 className="page-header__title">Receive Delivery — {delivery.deliveryReference}</h1><p className="muted">Confirm quantities and log physical material damage.</p></div><StatusBadge status={receipt?.status || delivery.status} tone="info" /></div>
    <Card><div className="context-ribbon"><div><span>Source purchase order</span><strong>PO #{delivery.purchaseOrderId}</strong></div><div><span>Supplier</span><strong>{delivery.supplierName}</strong></div><div><span>Project / destination</span><strong>{delivery.projectName || 'Not recorded'}</strong></div></div></Card>
    {receipt && <div role="status" className="advisory-note">Receipt saved: {receipt.status}. Only the evidence link remains to be saved; retrying will not receive the delivery again.</div>}
    <form className="workflow-columns" onSubmit={submit}>
      <div className="stack">
        <Card><div className="table-wrap"><table className="data-table receiving-table"><thead><tr><th>Item description</th><th>Ordered</th><th>Outstanding</th><th>Received Qty</th><th>Damaged Qty</th><th>Usable Preview</th></tr></thead><tbody>{items.map((item, index) => <tr key={item.purchaseOrderItemId}>
          <td><strong>{item.materialName}</strong><small className="muted">Unit: {item.materialUnit}</small><TextInput label={`Notes for ${item.materialName}`} name={`notes-${index}`} value={item.notes} disabled={busy || !!receipt} onChange={e => update(index, 'notes', e.target.value)} /></td>
          <td>{item.orderedQuantity}</td><td>{item.outstandingQuantity}</td>
          <td><input aria-label={`Received quantity ${index + 1}`} className="field__control" type="number" min="0" max={item.outstandingQuantity} step="0.01" required disabled={busy || !!receipt} value={item.receivedQuantity} onChange={e => update(index, 'receivedQuantity', e.target.value)} /></td>
          <td><input aria-label={`Damaged quantity ${index + 1}`} className="field__control" type="number" min="0" max={Number(item.receivedQuantity)} step="0.01" required disabled={busy || !!receipt} value={item.damagedQuantity} onChange={e => update(index, 'damagedQuantity', e.target.value)} /></td>
          <td><output aria-label={`Usable quantity ${index + 1}`}>{Math.max(0, Number(item.receivedQuantity || 0) - Number(item.damagedQuantity || 0))}</output></td>
        </tr>)}</tbody></table></div></Card>
        <Card><div className="stack"><TextInput label="Delivery receipt remarks" name="receipt-notes" multiline value={notes} disabled={busy || !!receipt} onChange={e => setNotes(e.target.value)} /><TextInput label="Existing evidence link (optional)" name="evidence" type="url" placeholder="https://" value={evidenceUrl} disabled={busy} onChange={e => setEvidenceUrl(e.target.value)} /><p className="muted">Stores a link to an existing photo or document. BuildWise does not upload or store the file.</p></div></Card>
        {validation && <div className="error-state" role="alert">Received quantity cannot exceed outstanding quantity. Damaged quantity cannot exceed received quantity.</div>}
        {error && <div className="error-state" role="alert">{error}</div>}
      </div>
      <aside className="workflow-summary"><Card title="Receipt Summary"><dl className="summary-facts"><dt>Ordered items</dt><dd>{items.length} lines</dd><dt>Total damaged</dt><dd>{damaged}</dd></dl><p className="advisory-note">{discrepancy ? 'Discrepancies detected — shortages or damage will be recorded.' : 'Quantities match the outstanding order.'}</p><div className="stack"><Button type="submit" disabled={busy || (!receipt && validation)}>{busy ? 'Saving...' : receipt ? 'Retry evidence link' : 'Confirm Receipt'}</Button><Button variant="secondary" onClick={receipt ? onSuccess : onCancel} disabled={busy}>{receipt ? 'Continue without evidence link' : 'Cancel'}</Button></div></Card></aside>
    </form>
  </section>
}

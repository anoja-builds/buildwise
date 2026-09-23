import { useEffect, useState } from 'react'
import { Button, Card, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import { statusTone } from '../components/statusTone'

const NEXT_STATUS = { Created: ['Confirmed', 'Cancelled'], Confirmed: ['InProgress', 'Cancelled'], InProgress: ['Completed', 'Cancelled'], Completed: [], Cancelled: [] }

export default function PurchaseOrderDetail({ orderId, onBack }) {
  const [order, setOrder] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [nextStatus, setNextStatus] = useState('')
  const [saving, setSaving] = useState(false)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await procurementApi.getPurchaseOrder(orderId)
      setOrder(data)
      setNextStatus('')
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [orderId])

  const handleStatusUpdate = async () => {
    if (!nextStatus) return
    setSaving(true)
    try {
      await procurementApi.updatePurchaseOrderStatus(orderId, nextStatus)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <LoadingState message="Loading purchase order…" />
  if (error) return <ErrorState message={error} onRetry={load} />
  if (!order) return null

  const options = NEXT_STATUS[order.status] || []

  return (
    <div className="stack">
      <button type="button" className="proc-back" onClick={onBack}>← Back to purchase orders</button>
      <PageHeader eyebrow={`PO-${order.id}`} title={order.supplierName} description={`Linked to quotation #${order.quotationId} and material request #${order.materialRequestId}.`} />
      <div className="actions"><StatusBadge status={statusTone(order.status)}>{order.status}</StatusBadge></div>

      <div className="grid grid--2">
        <Card title="Order information">
          {[['Order date', order.orderDate], ['Expected delivery', order.expectedDeliveryDate || 'Not set'], ['Total amount', order.totalAmount.toLocaleString()], ['Created', new Date(order.createdAt).toLocaleString()]].map(([label, value]) => (
            <div className="detail-row" key={label}><span className="detail-row__label">{label}</span><span className="detail-row__value">{value}</span></div>
          ))}
        </Card>
        <Card title="Update status">
          {options.length === 0 ? <p className="status-note">This order is in a final state and cannot be updated further.</p> : (
            <div className="form-grid">
              <SelectInput label="New status" value={nextStatus} onChange={(e) => setNextStatus(e.target.value)} options={[{ value: '', label: 'Select next status' }, ...options.map((o) => ({ value: o, label: o }))]} />
              <div className="form-actions form-span"><Button onClick={handleStatusUpdate} disabled={!nextStatus || saving}>{saving ? 'Saving…' : 'Update status'}</Button></div>
            </div>
          )}
        </Card>
      </div>

      <Card title="Order items">
        <div className="table-wrap">
          <table className="data-table">
            <thead><tr><th>Material</th><th>Quantity</th><th>Unit price</th><th>Line total</th></tr></thead>
            <tbody>
              {order.items.map((item) => (
                <tr key={item.id}><td>{item.materialName}</td><td>{item.orderedQuantity} {item.unit}</td><td>{item.unitPrice.toLocaleString()}</td><td>{item.lineTotal.toLocaleString()}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}

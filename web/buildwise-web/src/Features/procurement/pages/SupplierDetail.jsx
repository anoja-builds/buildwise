import { useEffect, useState } from 'react'
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import { statusTone } from '../components/statusTone'
import SupplierFormModal from '../components/SupplierFormModal'

export default function SupplierDetail({ supplierId, onBack }) {
  const [supplier, setSupplier] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [editOpen, setEditOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [statusSaving, setStatusSaving] = useState(false)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setSupplier(await procurementApi.getSupplier(supplierId))
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [supplierId])

  const handleSave = async (form) => {
    setSubmitting(true)
    try {
      await procurementApi.updateSupplier(supplierId, form)
      setEditOpen(false)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  const handleStatusChange = async (event) => {
    const next = event.target.value
    setStatusSaving(true)
    try {
      await procurementApi.updateSupplierStatus(supplierId, next)
      await load()
    } catch (err) {
      setError(err.message)
    } finally {
      setStatusSaving(false)
    }
  }

  if (loading) return <LoadingState message="Loading supplier…" />
  if (error) return <ErrorState message={error} onRetry={load} />
  if (!supplier) return null

  const actions = <><Button variant="secondary" onClick={onBack}>Back to list</Button><Button onClick={() => setEditOpen(true)}>Edit details</Button></>

  return (
    <div className="stack">
      <PageHeader eyebrow={`SUPPLIER #${supplier.id}`} title={supplier.name} description="Supplier profile and quotation history." actions={actions} />
      <div className="actions">
        <StatusBadge status={statusTone(supplier.status)}>{supplier.status}</StatusBadge>
        <SelectInput label="" aria-label="Change supplier status" value={supplier.status} onChange={handleStatusChange} disabled={statusSaving} options={[{ value: 'Active', label: 'Set Active' }, { value: 'Inactive', label: 'Set Inactive' }, { value: 'Suspended', label: 'Set Suspended' }]} />
      </div>
      <div className="grid grid--2">
        <Card title="Information">
          {[['Contact person', supplier.contactPerson || '—'], ['Email', supplier.email || '—'], ['Phone', supplier.phone || '—'], ['Address', supplier.address || '—'], ['Created', new Date(supplier.createdAt).toLocaleDateString()]].map(([label, value]) => (
            <div className="detail-row" key={label}><span className="detail-row__label">{label}</span><span className="detail-row__value">{value}</span></div>
          ))}
        </Card>
        <Card title="Quotation history" subtitle="Every quotation this supplier has submitted, across all requests.">
          {(!supplier.quotationHistory || supplier.quotationHistory.length === 0) ? (
            <EmptyState title="No quotations yet" message="This supplier hasn't submitted a quotation." />
          ) : (
            <ul className="activity-list">
              {supplier.quotationHistory.map((q) => (
                <li className="activity-item" style={{ alignItems: 'flex-start' }} key={q.quotationId}>
                  <span className="activity-dot" />
                  <div style={{ flex: 1 }}>
                    <p>Quotation #{q.quotationId} for request #{q.materialRequestId} — <strong>{Number(q.totalAmount).toLocaleString()}</strong></p>
                    <span className="activity-time">Valid until {q.validUntil}</span>
                  </div>
                  <StatusBadge status={statusTone(q.status)}>{q.status}</StatusBadge>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
      <SupplierFormModal open={editOpen} supplier={supplier} onCancel={() => setEditOpen(false)} onSubmit={handleSave} submitting={submitting} />
    </div>
  )
}

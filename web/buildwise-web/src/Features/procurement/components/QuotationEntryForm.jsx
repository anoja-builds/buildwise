import { useEffect, useMemo, useState } from 'react'
import { Button, Card, SelectInput, TextInput } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'

const today = () => new Date().toISOString().slice(0, 10)
const plusDays = (days) => {
  const d = new Date()
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

export default function QuotationEntryForm({ requestDetail, onCreated }) {
  const [suppliers, setSuppliers] = useState([])
  const [supplierId, setSupplierId] = useState('')
  const [quotationDate, setQuotationDate] = useState(today())
  const [validUntil, setValidUntil] = useState(plusDays(21))
  const [lines, setLines] = useState({})
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    procurementApi.listSuppliers({ status: 'Active', pageSize: 200 }).then((data) => setSuppliers(data.items)).catch(() => setSuppliers([]))
  }, [])

  const items = requestDetail?.items || []

  const total = useMemo(() => items.reduce((sum, item) => {
    const line = lines[item.id]
    const qty = Number(line?.quantity || 0)
    const price = Number(line?.unitPrice || 0)
    return sum + qty * price
  }, 0), [items, lines])

  const updateLine = (itemId, field) => (event) => {
    setLines((prev) => ({ ...prev, [itemId]: { ...prev[itemId], [field]: event.target.value } }))
  }

  const handleSubmit = async (event) => {
    event.preventDefault()
    setError('')

    if (!supplierId) { setError('Select a supplier.'); return }

    const quoteItems = items
      .map((item) => ({ materialRequestItemId: item.id, quantity: Number(lines[item.id]?.quantity || 0), unitPrice: Number(lines[item.id]?.unitPrice || 0) }))
      .filter((line) => line.quantity > 0 || line.unitPrice > 0)

    if (quoteItems.length === 0) { setError('Enter quantity and unit price for at least one item.'); return }
    if (quoteItems.some((l) => l.quantity <= 0 || l.unitPrice < 0)) { setError('Quantity must be positive and unit price cannot be negative.'); return }

    setSubmitting(true)
    try {
      await procurementApi.createQuotation(requestDetail.id, { supplierId: Number(supplierId), quotationDate, validUntil, items: quoteItems })
      setSupplierId('')
      setLines({})
      onCreated?.()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Card title="Record a quotation" subtitle="Enter what a supplier quoted for this request's line items. Total is calculated automatically.">
      <form onSubmit={handleSubmit} className="stack">
        <div className="form-grid">
          <SelectInput label="Supplier" name="supplierId" value={supplierId} onChange={(e) => setSupplierId(e.target.value)} options={[{ value: '', label: 'Select an active supplier' }, ...suppliers.map((s) => ({ value: String(s.id), label: s.name }))]} />
          <TextInput label="Quotation date" name="quotationDate" type="date" value={quotationDate} onChange={(e) => setQuotationDate(e.target.value)} required />
          <TextInput label="Valid until" name="validUntil" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} required />
        </div>

        <div>
          {items.map((item) => (
            <div className="item-line" key={item.id}>
              <div className="item-line__label">{item.materialName} <span className="muted">(requested {item.requestedQuantity} {item.unit})</span></div>
              <TextInput label={`Quantity (${item.unit})`} name={`quantity-${item.id}`} type="number" min="0" step="0.01" value={lines[item.id]?.quantity || ''} onChange={updateLine(item.id, 'quantity')} />
              <TextInput label="Unit price" name={`unitPrice-${item.id}`} type="number" min="0" step="0.01" value={lines[item.id]?.unitPrice || ''} onChange={updateLine(item.id, 'unitPrice')} />
              <div>
                <span className="field__label">Line total</span>
                <div style={{ fontWeight: 700, paddingTop: '0.6rem' }}>{(Number(lines[item.id]?.quantity || 0) * Number(lines[item.id]?.unitPrice || 0)).toLocaleString()}</div>
              </div>
            </div>
          ))}
        </div>

        <div className="total-strip"><span>Quotation total</span><span>{total.toLocaleString()}</span></div>

        {error && <span className="field__error">{error}</span>}

        <div className="form-actions">
          <Button type="submit" disabled={submitting}>{submitting ? 'Saving…' : 'Save quotation'}</Button>
        </div>
      </form>
    </Card>
  )
}

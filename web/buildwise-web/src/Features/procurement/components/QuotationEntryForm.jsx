import { useEffect, useMemo, useState } from 'react'
import { Button, Card, SelectInput, TextInput } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'

const todayStr = () => new Date().toISOString().slice(0, 10)
const plusDays = (days) => {
  const d = new Date()
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

/**
 * Date rules:
 *   quotationDate  — past or today ✅, future ❌
 *   validUntil     — must be after quotationDate ✅ (also: if already in the past → warning)
 *   promisedDeliveryDate — must be after quotationDate ✅
 */
function validateDates(quotationDate, validUntil, promisedDeliveryDate) {
  const errors = {}
  const today = todayStr()

  if (quotationDate > today) {
    errors.quotationDate = 'Quotation date cannot be in the future.'
  }

  if (quotationDate && validUntil) {
    if (validUntil <= quotationDate) {
      errors.validUntil = '"Valid until" must be after the quotation date.'
    } else if (validUntil < today) {
      errors.validUntilWarn = 'This quotation has already expired (valid-until is in the past).'
    }
  }

  if (quotationDate && promisedDeliveryDate) {
    if (promisedDeliveryDate <= quotationDate) {
      errors.promisedDeliveryDate = 'Promised delivery date must be after the quotation date.'
    }
  }

  return errors
}

export default function QuotationEntryForm({ requestDetail, onCreated }) {
  const [suppliers, setSuppliers] = useState([])
  const [supplierId, setSupplierId] = useState('')
  const [quotationDate, setQuotationDate] = useState(todayStr())
  const [validUntil, setValidUntil] = useState(plusDays(21))
  const [promisedDeliveryDate, setPromisedDeliveryDate] = useState(plusDays(5))
  const [lines, setLines] = useState({})
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    procurementApi
      .listSuppliers({ status: 'Active', pageSize: 200 })
      .then((data) => setSuppliers(data.items ?? data))
      .catch(() => setSuppliers([]))
  }, [])

  const items = requestDetail?.items || []

  const total = useMemo(
    () =>
      items.reduce((sum, item) => {
        const line = lines[item.id]
        return sum + Number(line?.quantity || 0) * Number(line?.unitPrice || 0)
      }, 0),
    [items, lines],
  )

  const dateErrors = validateDates(quotationDate, validUntil, promisedDeliveryDate)
  const hasDateError = !!(dateErrors.quotationDate || dateErrors.validUntil || dateErrors.promisedDeliveryDate)

  const updateLine = (itemId, field) => (event) => {
    setLines((prev) => ({ ...prev, [itemId]: { ...prev[itemId], [field]: event.target.value } }))
  }

  const handleSubmit = async (event) => {
    event.preventDefault()
    setError('')

    if (!supplierId) { setError('Select a supplier.'); return }
    if (hasDateError) { setError('Fix date errors before saving.'); return }

    const quoteItems = items
      .map((item) => ({
        materialRequestItemId: item.id,
        quantity: Number(lines[item.id]?.quantity || 0),
        unitPrice: Number(lines[item.id]?.unitPrice || 0),
      }))
      .filter((line) => line.quantity > 0 || line.unitPrice > 0)

    if (quoteItems.length === 0) { setError('Enter quantity and unit price for at least one item.'); return }
    if (quoteItems.some((l) => l.quantity <= 0 || l.unitPrice < 0)) {
      setError('Quantity must be positive and unit price cannot be negative.')
      return
    }

    setSubmitting(true)
    try {
      await procurementApi.createQuotation(requestDetail.id, {
        supplierId: Number(supplierId),
        quotationDate,
        validUntil,
        promisedDeliveryDate,
        items: quoteItems,
      })
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
    <Card
      title="Record a Quotation"
      subtitle="Enter what a supplier quoted for this request's line items. Total is calculated automatically."
    >
      <form onSubmit={handleSubmit} className="stack">
        {/* ── Dates grid ── */}
        <div className="form-grid">
          <SelectInput
            label="Supplier"
            name="supplierId"
            value={supplierId}
            onChange={(e) => setSupplierId(e.target.value)}
            options={[
              { value: '', label: 'Select an active supplier' },
              ...suppliers.map((s) => ({ value: String(s.id), label: s.name })),
            ]}
          />

          {/* Quotation date — past or today only */}
          <div className="field">
            <label className="field__label">
              Quotation date <span className="field__required">*</span>
            </label>
            <input
              className="field__control"
              type="date"
              name="quotationDate"
              required
              max={todayStr()}
              value={quotationDate}
              onChange={(e) => setQuotationDate(e.target.value)}
              aria-invalid={dateErrors.quotationDate ? 'true' : undefined}
            />
            {dateErrors.quotationDate
              ? <span className="field__error">⚠ {dateErrors.quotationDate}</span>
              : <span className="field__hint">Must be today or in the past.</span>}
          </div>

          {/* Valid until — must be after quotationDate */}
          <div className="field">
            <label className="field__label">
              Valid until <span className="field__required">*</span>
            </label>
            <input
              className="field__control"
              type="date"
              name="validUntil"
              required
              min={quotationDate ? (() => { const d = new Date(quotationDate); d.setDate(d.getDate() + 1); return d.toISOString().slice(0, 10) })() : undefined}
              value={validUntil}
              onChange={(e) => setValidUntil(e.target.value)}
              aria-invalid={dateErrors.validUntil ? 'true' : undefined}
            />
            {dateErrors.validUntil
              ? <span className="field__error">⚠ {dateErrors.validUntil}</span>
              : dateErrors.validUntilWarn
                ? <span className="field__error" style={{ color: 'var(--color-warning-700)' }}>⚠ {dateErrors.validUntilWarn}</span>
                : <span className="field__hint">Must be after the quotation date.</span>}
          </div>

          {/* Promised delivery date — must be after quotationDate */}
          <div className="field">
            <label className="field__label">
              Promised delivery date <span className="field__required">*</span>
            </label>
            <input
              className="field__control"
              type="date"
              name="promisedDeliveryDate"
              required
              min={quotationDate ? (() => { const d = new Date(quotationDate); d.setDate(d.getDate() + 1); return d.toISOString().slice(0, 10) })() : undefined}
              value={promisedDeliveryDate}
              onChange={(e) => setPromisedDeliveryDate(e.target.value)}
              aria-invalid={dateErrors.promisedDeliveryDate ? 'true' : undefined}
            />
            {dateErrors.promisedDeliveryDate
              ? <span className="field__error">⚠ {dateErrors.promisedDeliveryDate}</span>
              : <span className="field__hint">Must be after the quotation date.</span>}
          </div>
        </div>

        {/* ── Line items ── */}
        <div
          style={{
            background: 'var(--color-surface-muted)',
            borderRadius: 'var(--radius-md)',
            padding: '1rem',
            display: 'grid',
            gap: '1rem',
          }}
        >
          <strong style={{ fontSize: 'var(--font-sm)', color: 'var(--color-text-muted)' }}>
            LINE ITEMS
          </strong>
          {items.map((item) => (
            <div
              className="item-line"
              key={item.id}
              style={{
                background: 'var(--color-white)',
                borderRadius: 'var(--radius-md)',
                border: '1px solid var(--color-border)',
                padding: '0.75rem 1rem',
                display: 'grid',
                gridTemplateColumns: '2fr 1fr 1fr 1fr',
                gap: '0.75rem',
                alignItems: 'end',
              }}
            >
              <div className="item-line__label">
                <span style={{ fontWeight: 700 }}>{item.materialName}</span>
                <br />
                <span className="muted" style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                  Requested: {item.requestedQuantity} {item.unit}
                </span>
              </div>
              <TextInput
                label={`Quantity (${item.unit})`}
                name={`quantity-${item.id}`}
                type="number"
                min="0"
                step="0.01"
                value={lines[item.id]?.quantity || ''}
                onChange={updateLine(item.id, 'quantity')}
              />
              <TextInput
                label="Unit price"
                name={`unitPrice-${item.id}`}
                type="number"
                min="0"
                step="0.01"
                value={lines[item.id]?.unitPrice || ''}
                onChange={updateLine(item.id, 'unitPrice')}
              />
              <div>
                <span className="field__label">Line total</span>
                <div
                  style={{
                    fontWeight: 800,
                    paddingTop: '0.6rem',
                    color: 'var(--color-primary-700)',
                    fontSize: '1.05rem',
                  }}
                >
                  <span className="muted" style={{ fontSize: '0.85rem', marginRight: '0.25rem' }}>LKR</span>
                  <span>{(Number(lines[item.id]?.quantity || 0) * Number(lines[item.id]?.unitPrice || 0)).toLocaleString()}</span>
                </div>
              </div>
            </div>
          ))}
        </div>

        <div
          className="total-strip"
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            background: 'linear-gradient(180deg, var(--color-primary-700), var(--color-primary-800))',
            color: 'var(--color-white)',
            borderRadius: 'var(--radius-md)',
            padding: '0.9rem 1.25rem',
            fontWeight: 700,
          }}
        >
          <span>Quotation Total</span>
          <span style={{ fontSize: '1.2rem' }}>
            <span style={{ fontSize: '0.9rem', opacity: 0.85, marginRight: '0.25rem' }}>LKR</span>
            <span>{total.toLocaleString()}</span>
          </span>
        </div>

        {error && <span className="field__error" style={{ fontSize: '0.9rem' }}>{error}</span>}

        <div className="form-actions">
          <Button type="submit" disabled={submitting || hasDateError}>
            {submitting ? 'Saving…' : 'Save quotation'}
          </Button>
        </div>
      </form>
    </Card>
  )
}

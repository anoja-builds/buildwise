import { useEffect, useRef, useState } from 'react'
import { deliveryService } from '../services/deliveryService'

/**
 * Modal form to schedule a delivery for a Confirmed/InProgress Purchase Order.
 * Calls POST /api/Deliveries via deliveryService.scheduleDelivery.
 * All business validation is enforced by the backend; here we only do minimal
 * UX validation (non-empty reference, valid PO selected).
 *
 * Props:
 *   onCancel  () => void
 *   onSuccess () => void   – called after successful creation; caller refreshes
 */
export default function ScheduleDeliveryModal({ onCancel, onSuccess }) {
  const [pos, setPos] = useState([])
  const [loadingPos, setLoadingPos] = useState(true)
  const [posError, setPosError] = useState('')

  const [selectedPo, setSelectedPo] = useState('')
  const [reference, setReference] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const locked = useRef(false)
  const active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])

  useEffect(() => {
    setLoadingPos(true)
    deliveryService.getConfirmedPOs()
      .then((items) => { if (active.current) { setPos(items); setLoadingPos(false) } })
      .catch((err) => { if (active.current) { setPosError(err.message); setLoadingPos(false) } })
  }, [])

  async function handleSubmit(e) {
    e.preventDefault()
    if (!selectedPo) { setError('Select a purchase order.'); return }
    if (!reference.trim()) { setError('Enter a delivery reference.'); return }
    if (locked.current) return
    locked.current = true
    setSubmitting(true)
    setError('')
    try {
      await deliveryService.scheduleDelivery(Number(selectedPo), reference.trim())
      if (active.current) onSuccess()
    } catch (err) {
      if (active.current) setError(err.message)
    } finally {
      locked.current = false
      if (active.current) setSubmitting(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="schedule-modal-title"
      style={{
        position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000
      }}
    >
      <div className="card" style={{ minWidth: 360, maxWidth: 480, width: '100%', padding: 24 }}>
        <h2 id="schedule-modal-title" style={{ marginTop: 0 }}>Schedule Delivery</h2>

        {loadingPos && <p>Loading purchase orders…</p>}
        {posError && <p style={{ color: '#c0392b' }}>⚠️ {posError}</p>}

        {!loadingPos && !posError && (
          <form onSubmit={handleSubmit} className="stack">
            <div>
              <label htmlFor="schedule-po" style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
                Purchase Order (Confirmed / In Progress)
              </label>
              {pos.length === 0 ? (
                <p style={{ color: '#888', marginTop: 0 }}>
                  No confirmed or in-progress purchase orders found.
                </p>
              ) : (
                <select
                  id="schedule-po"
                  value={selectedPo}
                  onChange={(e) => setSelectedPo(e.target.value)}
                  disabled={submitting}
                  style={{ width: '100%', padding: '8px', borderRadius: 4, border: '1px solid #ccc' }}
                >
                  <option value="">— Select a PO —</option>
                  {pos.map((po) => (
                    <option key={po.id} value={po.id}>
                      PO-{po.id} — {po.supplierName || `Supplier #${po.supplierId}`} ({po.status})
                    </option>
                  ))}
                </select>
              )}
            </div>

            <div>
              <label htmlFor="schedule-ref" style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
                Delivery Reference
              </label>
              <input
                id="schedule-ref"
                type="text"
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                placeholder="e.g. DEL-2026-001"
                disabled={submitting}
                style={{ width: '100%', padding: '8px', borderRadius: 4, border: '1px solid #ccc', boxSizing: 'border-box' }}
              />
            </div>

            {error && (
              <p role="alert" style={{ color: '#c0392b', margin: 0 }}>
                ⚠️ {error}
              </p>
            )}

            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button
                type="button"
                className="btn btn--secondary"
                onClick={onCancel}
                disabled={submitting}
              >
                Cancel
              </button>
              <button
                type="submit"
                className="btn btn--primary"
                disabled={submitting || pos.length === 0}
              >
                {submitting ? 'Scheduling…' : 'Schedule Delivery'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}

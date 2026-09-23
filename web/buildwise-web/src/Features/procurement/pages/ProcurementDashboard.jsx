import { useEffect, useState } from 'react'
import { Card, ErrorState, LoadingState, PageHeader, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import { statusTone } from '../components/statusTone'

export default function ProcurementDashboard({ onOpenRequest, onOpenOrder }) {
  const [requests, setRequests] = useState([])
  const [orders, setOrders] = useState([])
  const [ordersTotal, setOrdersTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      const [reqs, pos] = await Promise.all([
        procurementApi.listApprovedMaterialRequests(),
        procurementApi.listPurchaseOrders({ pageSize: 200 })
      ])
      setRequests(reqs)
      setOrders(pos.items)
      setOrdersTotal(pos.total)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  if (loading) return <LoadingState message="Loading procurement dashboard…" />
  if (error) return <ErrorState message={error} onRetry={load} />

  const pendingQuotes = requests.filter((r) => r.quotationCount === 0).length
  const recentOrders = [...orders].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt)).slice(0, 5)
  const summaries = [
    ['Approved requests queued', requests.length, 'Awaiting supplier quotations', 'var(--color-primary-900)'],
    ['Requests without quotes', pendingQuotes, 'No quotation recorded yet', 'var(--color-warning-700)'],
    ['Purchase orders total', ordersTotal, 'All time', 'var(--color-success-700)'],
    ['Open purchase orders', orders.filter((o) => o.status !== 'Completed' && o.status !== 'Cancelled').length, 'Created through In progress', 'var(--color-info-700)']
  ]

  return (
    <div className="stack">
      <PageHeader title="Procurement Dashboard" description="Executive summary of pending quotations, workflows, and recent purchase orders." />

      {/* Top metric cards — clean white tiles with a 5px left accent border */}
      <div className="grid grid--4">
        {summaries.map(([label, value, note, accent]) => (
          <Card key={label} className="metric-card" style={{ '--metric-accent': accent }}>
            <p className="metric-card__label">{label}</p>
            <p className="metric-card__value">{value}</p>
            <p className="metric-card__note">{note}</p>
          </Card>
        ))}
      </div>

      {/* Main workspace — two rounded containers, no inner tab strip */}
      <div className="grid grid--2">
        <Card title="Approved requests awaiting quotations" subtitle="Click a request to open its procurement workspace.">
          {requests.length === 0 ? <p className="status-note">Nothing in the queue right now.</p> : (
            <ul className="board-list">
              {requests.map((r) => (
                <li key={r.id}>
                  <button type="button" className="board-item" onClick={() => onOpenRequest(r.id)}>
                    <span className="board-item__body">
                      <span className="board-item__title"><span className="board-dot" aria-hidden="true" />MR-{r.id} — {r.projectName}</span>
                      <span className="board-item__meta">{r.quotationCount} quotation(s) · required by {r.requiredDate}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Recent purchase orders" subtitle="Most recently created purchase orders.">
          {recentOrders.length === 0 ? <p className="status-note">No purchase orders yet.</p> : (
            <ul className="board-list">
              {recentOrders.map((po) => (
                <li key={po.id}>
                  <button type="button" className="board-item" onClick={() => onOpenOrder(po.id)}>
                    <span className="board-item__body">
                      <span className="board-item__title"><span className="board-dot" aria-hidden="true" />PO-{po.id} — {po.supplierName}</span>
                      <span className="board-item__meta">{po.totalAmount.toLocaleString()} · {po.orderDate}</span>
                    </span>
                    <StatusBadge status={statusTone(po.status)}>{po.status}</StatusBadge>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  )
}

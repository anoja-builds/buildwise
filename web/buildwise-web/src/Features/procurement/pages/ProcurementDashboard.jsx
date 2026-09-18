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
    ['Approved requests queued', requests.length, 'Awaiting supplier quotations', 'var(--color-primary-700)'],
    ['Requests without quotes', pendingQuotes, 'No quotation recorded yet', 'var(--color-warning-700)'],
    ['Purchase orders total', ordersTotal, 'All time', 'var(--color-success-700)'],
    ['Open purchase orders', orders.filter((o) => o.status !== 'Completed' && o.status !== 'Cancelled').length, 'Created through In progress', 'var(--color-info-700)']
  ]

  return (
    <div className="stack">
      <PageHeader title="Procurement Dashboard" description="Executive summary of pending quotations, workflows, and recent purchase orders." />
      <div className="grid grid--4">
        {summaries.map(([label, value, note, color]) => (
          <Card key={label} className="summary-card" style={{ '--summary-color': color }}>
            <div className="summary-card__label">{label}</div>
            <div className="summary-card__value">{value}</div>
            <div className="summary-card__note">{note}</div>
          </Card>
        ))}
      </div>
      <div className="grid grid--2">
        <Card title="Approved requests awaiting quotations" subtitle="Click a request to open its procurement workspace.">
          {requests.length === 0 ? <p className="status-note">Nothing in the queue right now.</p> : (
            <ul className="activity-list">
              {requests.map((r) => (
                <li className="activity-item" key={r.id}>
                  <span className="activity-dot" />
                  <div style={{ flex: 1 }}>
                    <p><button className="table-action" style={{ padding: 0 }} onClick={() => onOpenRequest(r.id)}>MR-{r.id} — {r.projectName}</button></p>
                    <span className="activity-time">{r.quotationCount} quotation(s) · required by {r.requiredDate}</span>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Recent purchase orders" subtitle="Most recently created purchase orders.">
          {recentOrders.length === 0 ? <p className="status-note">No purchase orders yet.</p> : (
            <ul className="activity-list">
              {recentOrders.map((po) => (
                <li className="activity-item" style={{ alignItems: 'flex-start' }} key={po.id}>
                  <span className="activity-dot" />
                  <div style={{ flex: 1 }}>
                    <p><button className="table-action" style={{ padding: 0 }} onClick={() => onOpenOrder(po.id)}>PO-{po.id} — {po.supplierName}</button></p>
                    <span className="activity-time">{po.totalAmount.toLocaleString()} · {po.orderDate}</span>
                  </div>
                  <StatusBadge status={statusTone(po.status)}>{po.status}</StatusBadge>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  )
}

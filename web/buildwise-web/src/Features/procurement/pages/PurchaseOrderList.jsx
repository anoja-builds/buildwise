import { useEffect, useState } from 'react'
import { Card, EmptyState, ErrorState, LoadingState, PageHeader, Pagination, SearchInput, SelectInput, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import { statusTone } from '../components/statusTone'

const PAGE_SIZE = 10

export default function PurchaseOrderList({ onOpenOrder }) {
  const [orders, setOrders] = useState([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('all')

  const load = async (targetPage = page) => {
    setLoading(true)
    setError(null)
    try {
      const data = await procurementApi.listPurchaseOrders({ search: search || undefined, status: status === 'all' ? undefined : status, page: targetPage, pageSize: PAGE_SIZE })
      setOrders(data.items)
      setTotal(data.total)
      setPage(data.page)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load(1) }, [status])

  return (
    <div className="stack">
      <PageHeader title="Purchase Orders" description="Purchase orders generated from approved procurement recommendations." />
      <div className="toolbar">
        <form className="toolbar__filters" onSubmit={(e) => { e.preventDefault(); load(1) }}>
          <SearchInput placeholder="Search by supplier or PO number…" value={search} onChange={(e) => setSearch(e.target.value)} />
          <SelectInput label="Status" value={status} onChange={(e) => setStatus(e.target.value)} options={[{ value: 'all', label: 'All statuses' }, { value: 'Created', label: 'Created' }, { value: 'Confirmed', label: 'Confirmed' }, { value: 'InProgress', label: 'In progress' }, { value: 'Completed', label: 'Completed' }, { value: 'Cancelled', label: 'Cancelled' }]} />
        </form>
      </div>
      <Card>
        {loading ? <LoadingState message="Loading purchase orders…" /> : error ? <ErrorState message={error} onRetry={() => load()} /> : orders.length === 0 ? (
          <EmptyState title="No purchase orders yet" message="Purchase orders appear here once a Procurement Manager approves a recommendation." />
        ) : (
          <>
            <div className="table-wrap">
              <table className="data-table">
                <thead><tr><th>PO</th><th>Supplier</th><th>Order date</th><th>Expected delivery</th><th>Total</th><th>Status</th><th>Actions</th></tr></thead>
                <tbody>
                  {orders.map((po) => (
                    <tr key={po.id}>
                      <td><strong>PO-{po.id}</strong></td>
                      <td>{po.supplierName}</td>
                      <td>{po.orderDate}</td>
                      <td>{po.expectedDeliveryDate || '—'}</td>
                      <td>{po.totalAmount.toLocaleString()}</td>
                      <td><StatusBadge status={statusTone(po.status)}>{po.status}</StatusBadge></td>
                      <td><button className="table-action" onClick={() => onOpenOrder(po.id)}>View</button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={(next) => load(next)} />
          </>
        )}
      </Card>
    </div>
  )
}

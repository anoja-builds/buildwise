import { useEffect, useState } from 'react'
import { Card, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'

export default function ApprovedRequestsQueue({ onOpenRequest }) {
  const [requests, setRequests] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setRequests(await procurementApi.listApprovedMaterialRequests())
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  return (
    <div className="stack">
      <PageHeader title="Approved Requests Queue" description="Approved material requests ready for supplier quotations, per project." />
      <Card>
        {loading ? <LoadingState message="Loading approved requests…" /> : error ? <ErrorState message={error} onRetry={load} /> : requests.length === 0 ? (
          <EmptyState title="Nothing awaiting quotations" message="Approved material requests will appear here once Component 1 approves them." />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>Request</th><th>Project</th><th>Required by</th><th>Reason</th><th>Quotations so far</th><th>Status</th><th>Actions</th></tr></thead>
              <tbody>
                {requests.map((r) => (
                  <tr key={r.id}>
                    <td><strong>MR-{r.id}</strong></td>
                    <td>{r.projectName}</td>
                    <td>{r.requiredDate}</td>
                    <td>{r.reason || '—'}</td>
                    <td>{r.quotationCount}</td>
                    <td><StatusBadge status="success">{r.status}</StatusBadge></td>
                    <td><button className="table-action" onClick={() => onOpenRequest(r.id)}>Open workspace</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}

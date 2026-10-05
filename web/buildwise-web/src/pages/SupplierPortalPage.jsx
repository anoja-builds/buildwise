import { Fragment, useEffect, useState } from 'react'
import { Card, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from '../components/shared'
import { statusTone } from '../Features/procurement/components/statusTone'
import { supplierPortalApi } from '../services/supplierPortalApi'

/**
 * Supplier portal shell. Renders one of three sections based on the route's
 * `section` prop, mirroring how ProcurementApp is structured.
 *
 * Everything shown here is already scoped to the signed-in supplier by the API,
 * so this component never filters by supplier itself.
 */
export default function SupplierPortal({ section = 'Rfqs' }) {
  if (section === 'Quotations') return <MyQuotations />
  if (section === 'PurchaseOrders') return <MyPurchaseOrders />
  return <MyRfqs />
}

function MyRfqs() {
  const [rfqs, setRfqs] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [expanded, setExpanded] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try { setRfqs(await supplierPortalApi.listRfqs()) }
    catch (err) { setError(err.message) }
    finally { setLoading(false) }
  }

  useEffect(() => { load() }, [])

  const open = async (rfq) => {
    if (expanded?.rfqId === rfq.rfqId) return setExpanded(null)
    setExpanded({ rfqId: rfq.rfqId, loading: true, items: [] })
    try {
      const items = await supplierPortalApi.getRfqItems(rfq.rfqId)
      setExpanded({ rfqId: rfq.rfqId, loading: false, items })
    } catch (err) {
      setError(err.message)
      setExpanded(null)
    }
  }

  return (
    <div className="stack">
      <PageHeader
        title="My RFQs"
        description="Requests your organisation has been invited to quote against."
      />
      {loading ? <LoadingState message="Loading your RFQs…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={load} /> : null}
      {!loading && !error && rfqs.length === 0 ? (
        <EmptyState
          title="No RFQs yet"
          message="You will see an invitation here once the procurement desk issues an RFQ to your organisation."
        />
      ) : null}

      {!loading && !error && rfqs.length > 0 ? (
        <Card>
          <table className="data-table">
            <thead>
              <tr>
                <th>RFQ</th><th>Project</th><th>Required by</th>
                <th>Status</th><th>Response</th><th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rfqs.map((rfq) => (
                <Fragment key={rfq.rfqId}>
                  <tr>
                    <td><strong>RFQ-{rfq.rfqId}</strong></td>
                    <td>{rfq.projectName ?? '—'}</td>
                    <td>{rfq.requiredResponseDate}</td>
                    <td><StatusBadge status={statusTone(rfq.rfqStatus)}>{rfq.rfqStatus}</StatusBadge></td>
                    <td>
                      <StatusBadge status={statusTone(rfq.invitationStatus)}>{rfq.invitationStatus}</StatusBadge>
                    </td>
                    <td>
                      <button className="table-action" onClick={() => open(rfq)}>
                        {expanded?.rfqId === rfq.rfqId ? 'Hide lines' : 'View lines'}
                      </button>
                    </td>
                  </tr>
                  {expanded?.rfqId === rfq.rfqId ? (
                    <tr>
                      <td colSpan={6}>
                        {expanded.loading ? (
                          <LoadingState message="Loading request lines…" />
                        ) : (
                          <table className="data-table">
                            <thead>
                              <tr><th>Line</th><th>Material</th><th>Quantity</th><th>Notes</th></tr>
                            </thead>
                            <tbody>
                              {expanded.items.map((item) => (
                                <tr key={item.id}>
                                  <td>#{item.id}</td>
                                  <td>{item.materialName}</td>
                                  <td>{item.requestedQuantity} {item.materialUnit}</td>
                                  <td>{item.notes || '—'}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        )}
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        </Card>
      ) : null}
    </div>
  )
}
function MyQuotations() {
  const [rows, setRows] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try { setRows(await supplierPortalApi.listQuotations()) }
    catch (err) { setError(err.message) }
    finally { setLoading(false) }
  }

  useEffect(() => { load() }, [])

  return (
    <div className="stack">
      <PageHeader title="My Quotations" description="Quotations your organisation has submitted." />
      {loading ? <LoadingState message="Loading your quotations…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={load} /> : null}
      {!loading && !error && rows.length === 0 ? (
        <EmptyState title="No quotations yet" message="Quotations you submit against an open RFQ will appear here." />
      ) : null}
      {!loading && !error && rows.length > 0 ? (
        <Card>
          <table className="data-table">
            <thead>
              <tr>
                <th>Quotation</th><th>RFQ</th><th>Date</th><th>Valid until</th>
                <th>Total</th><th>Status</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((q) => (
                <tr key={q.id}>
                  <td><strong>Q-{q.id}</strong></td>
                  <td>{q.rfqId ? `RFQ-${q.rfqId}` : '—'}</td>
                  <td>{q.quotationDate}</td>
                  <td>{q.validUntil}</td>
                  <td>{Number(q.totalAmount).toLocaleString()}</td>
                  <td><StatusBadge status={statusTone(q.status)}>{q.status}</StatusBadge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      ) : null}
    </div>
  )
}

function MyPurchaseOrders() {
  const [rows, setRows] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try { setRows(await supplierPortalApi.listPurchaseOrders()) }
    catch (err) { setError(err.message) }
    finally { setLoading(false) }
  }

  useEffect(() => { load() }, [])

  return (
    <div className="stack">
      <PageHeader title="My Purchase Orders" description="Orders awarded to your organisation." />
      {loading ? <LoadingState message="Loading your orders…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={load} /> : null}
      {!loading && !error && rows.length === 0 ? (
        <EmptyState title="No purchase orders yet" message="An order appears here once your quotation is selected." />
      ) : null}
      {!loading && !error && rows.length > 0 ? (
        <Card>
          <table className="data-table">
            <thead>
              <tr>
                <th>PO</th><th>Order date</th><th>Expected</th><th>Total</th><th>Status</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((po) => (
                <tr key={po.id}>
                  <td><strong>PO-{po.id}</strong></td>
                  <td>{po.orderDate}</td>
                  <td>{po.expectedDeliveryDate || '—'}</td>
                  <td>{Number(po.totalAmount).toLocaleString()}</td>
                  <td><StatusBadge status={statusTone(po.status)}>{po.status}</StatusBadge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      ) : null}
    </div>
  )
}

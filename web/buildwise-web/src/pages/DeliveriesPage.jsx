import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeader,
  SelectInput,
  StatusBadge,
  TextInput,
} from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'
import './common/common.css'
import './DeliveriesPage.css'

const VIEWS = ['dashboard', 'deliveries', 'list', 'form', 'detail', 'ui-states']
const VIEW_LABELS = {
  dashboard: 'Dashboard',
  deliveries: 'Deliveries',
  list: 'List',
  form: 'Form',
  detail: 'Detail',
  'ui-states': 'UI States',
}
const STATUS_TONE = {
  Scheduled: 'neutral', InTransit: 'info', Arrived: 'info',
  ReceivingInProgress: 'warning', Received: 'success', PartiallyReceived: 'warning',
  DiscrepancyReported: 'danger', Confirmed: 'info', Cancelled: 'neutral',
}
const EMPTY_FORM = { purchaseOrderId: '', deliveryReference: '', notes: '' }

const formatDate = (value, withTime = false) => value
  ? new Intl.DateTimeFormat('en-GB', withTime ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium' }).format(new Date(value))
  : 'Not recorded'
const formatNumber = (value) => new Intl.NumberFormat('en-LK').format(Number(value ?? 0))
const titleCase = (value) => String(value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2')
const initials = (name) => (name || 'BuildWise').split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase()

export default function DeliveriesPage() {
  const { hasRole, user } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const requestedView = searchParams.get('view')
  const activeView = VIEWS.includes(requestedView) ? requestedView : 'dashboard'
  const [deliveries, setDeliveries] = useState([])
  const [purchaseOrders, setPurchaseOrders] = useState([])
  const [selectedId, setSelectedId] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const canReceive = hasRole('SiteEngineer') || hasRole('SiteOfficer')

  async function loadWorkspace() {
    setLoading(true)
    setError(null)
    try {
      const [deliveryRows, poRows] = await Promise.all([
        qualityApi.listDeliveries(),
        qualityApi.listConfirmedPurchaseOrders(),
      ])
      const normalizedDeliveries = Array.isArray(deliveryRows) ? deliveryRows : (deliveryRows.deliveries ?? [])
      const normalizedOrders = Array.isArray(poRows) ? poRows : (poRows.items ?? [])
      setDeliveries(normalizedDeliveries)
      setPurchaseOrders(normalizedOrders)
      setSelectedId((current) => current ?? normalizedDeliveries[0]?.id ?? null)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { loadWorkspace() }, [])

  const selectedDelivery = useMemo(
    () => deliveries.find((delivery) => delivery.id === Number(selectedId)) ?? null,
    [deliveries, selectedId],
  )

  function selectView(view) {
    setSearchParams(view === 'dashboard' ? {} : { view }, { replace: true })
  }

  function openDetail(id) {
    setSelectedId(id)
    selectView('detail')
  }

  if (loading) return <LoadingState message="Loading delivery workspace…" />
  if (error) return <ErrorState title="Could not load deliveries" message={error} onRetry={loadWorkspace} />

  return (
    <div className="delivery-workspace">
      <nav className="component-tabs" aria-label="Component 3 pages">
        {VIEWS.map((view) => (
          <button key={view} type="button" className={activeView === view ? 'is-active' : ''} onClick={() => selectView(view)}>
            {VIEW_LABELS[view]}
          </button>
        ))}
      </nav>

      {activeView === 'dashboard' && <DeliveryDashboard deliveries={deliveries} orders={purchaseOrders} onOpen={openDetail} onView={selectView} />}
      {activeView === 'deliveries' && <DeliveriesOverview deliveries={deliveries} orders={purchaseOrders} canReceive={canReceive} onRecord={() => selectView('form')} />}
      {activeView === 'list' && <DeliveryList deliveries={deliveries} onOpen={openDetail} />}
      {activeView === 'form' && (
        canReceive
          ? <ReceiveDeliveryForm orders={purchaseOrders} userName={user?.fullName} onCancel={() => selectView('deliveries')} onRecorded={(created) => { loadWorkspace(); setSelectedId(created.id); selectView('detail') }} />
          : <ReadOnlyNotice title="Receiving is restricted" message="Only Site Engineers and Site Officers can record a delivery. Your current role can still view Component 3 information." />
      )}
      {activeView === 'detail' && <DeliveryDetail delivery={selectedDelivery} onOpen={openDetail} deliveries={deliveries} />}
      {activeView === 'ui-states' && <DeliveryUiStates onAction={() => selectView('form')} />}
    </div>
  )
}

// ------------------------------------------------------------------ Dashboard

function DeliveryDashboard({ deliveries, orders, onOpen, onView }) {
  const today = new Date().toDateString()
  const received = deliveries.filter((item) => item.status === 'Received').length
  const attention = deliveries.filter((item) => item.status === 'DiscrepancyReported').length
  const completedToday = deliveries.filter((item) => new Date(item.deliveredAt).toDateString() === today).length
  const completion = deliveries.length ? Math.round((received / deliveries.length) * 100) : 0
  const discrepancyRate = deliveries.length ? Math.round((attention / deliveries.length) * 100) : 0
  const completedPoIds = new Set(deliveries.map((item) => item.purchaseOrderId))
  const awaiting = orders.filter((po) => !completedPoIds.has(po.id)).length
  const recent = deliveries.slice(0, 4)

  return (
    <section className="delivery-page">
      <PageHeader title="Dashboard" description="A live overview of Component 3 delivery activity." />
      <div className="metric-grid">
        <Metric label="Confirmed orders awaiting delivery" value={awaiting} note={`${orders.length} confirmed purchase orders`} tone="orange" />
        <Metric label="Recorded deliveries" value={deliveries.length} note="Across the current workspace" tone="blue" />
        <Metric label="Attention required" value={attention} note="Discrepancies need review" tone="red" />
        <Metric label="Completed today" value={completedToday} note="Persisted receiving records" tone="green" />
      </div>
      <div className="dashboard-columns">
        <Card title="Recent activity" subtitle="Newest delivery entries from PostgreSQL">
          {recent.length ? <ul className="activity-list">{recent.map((item) => (
            <li className="activity-item" key={item.id}>
              <span className="activity-dot" />
              <button className="activity-link" type="button" onClick={() => onOpen(item.id)}>
                <strong>Delivery #{item.id} · {item.deliveryReference}</strong>
                <span className="activity-time">{formatDate(item.deliveredAt, true)}</span>
              </button>
            </li>
          ))}</ul> : <EmptyState title="No recent activity" message="Recorded deliveries will appear here." />}
        </Card>
        <Card title="Workflow overview" subtitle="Calculated from current delivery records">
          <Progress label="Received without discrepancy" value={completion} tone="blue" />
          <Progress label="Discrepancy rate" value={discrepancyRate} tone={discrepancyRate ? 'orange' : 'green'} />
          <Progress label="Confirmed orders not yet received" value={orders.length ? Math.round((awaiting / orders.length) * 100) : 0} tone="blue" />
          <Button variant="secondary" onClick={() => onView('list')}>Review all deliveries</Button>
        </Card>
      </div>
    </section>
  )
}

function Metric({ label, value, note, tone }) {
  return <Card className={`metric-card metric-card--${tone}`}><span>{label}</span><strong>{value}</strong><small>{note}</small></Card>
}

function Progress({ label, value, tone }) {
  return <div className="delivery-progress"><div><span>{label}</span><StatusBadge status={tone === 'orange' ? 'warning' : tone === 'green' ? 'success' : 'info'}>{value}%</StatusBadge></div><div className="delivery-progress__track"><i style={{ width: `${Math.min(value, 100)}%` }} /></div></div>
}

function DeliveryStatus({ status }) {
  return <StatusBadge status={STATUS_TONE[status] ?? 'neutral'}>{titleCase(status)}</StatusBadge>
}

function itemSummary(delivery) {
  const count = delivery.items?.length ?? 0
  return `${count} line ${count === 1 ? 'item' : 'items'}`
}

function deliverySupplier(delivery) {
  return delivery.purchaseOrder?.supplier?.name ?? delivery.purchaseOrder?.supplierName ?? 'Supplier record not expanded'
}

// ------------------------------------------------------------------ Overview

function DeliveriesOverview({ deliveries, orders, canReceive, onRecord }) {
  return (
    <section className="delivery-page">
      <PageHeader
        title="Deliveries"
        description="Component 3 — monitor confirmed orders, receiving records, and discrepancies."
        actions={canReceive ? <Button onClick={onRecord}>+ Record delivery</Button> : <StatusBadge status="neutral">View only</StatusBadge>}
      />
      <div className="overview-grid">
        <Card title="Confirmed purchase orders" subtitle="Eligible to receive on site">
          {orders.length ? orders.slice(0, 6).map((po) => (
            <div className="compact-row" key={po.id}><div><strong>PO-{po.id}</strong><small>{po.items?.length ?? 0} line item(s) · due {formatDate(po.expectedDeliveryDate)}</small></div><StatusBadge status="info">Confirmed</StatusBadge></div>
          )) : <EmptyState title="No confirmed orders" message="Confirmed purchase orders will become available for receiving." />}
        </Card>
        <Card title="Latest receiving" subtitle="Most recent records from the shared API">
          {deliveries.length ? deliveries.slice(0, 6).map((delivery) => (
            <div className="compact-row" key={delivery.id}><div><strong>{delivery.deliveryReference}</strong><small>PO-{delivery.purchaseOrderId} · {itemSummary(delivery)}</small></div><DeliveryStatus status={delivery.status} /></div>
          )) : <EmptyState title="Nothing received yet" message="The first recorded delivery will appear here." actionLabel={canReceive ? 'Record first delivery' : undefined} onAction={onRecord} />}
        </Card>
      </div>
    </section>
  )
}

// ------------------------------------------------------------------ List

function DeliveryList({ deliveries, onOpen }) {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('All')
  const [sort, setSort] = useState('newest')
  const filtered = useMemo(() => deliveries
    .filter((delivery) => status === 'All' || delivery.status === status)
    .filter((delivery) => `${delivery.deliveryReference} ${delivery.purchaseOrderId} ${deliverySupplier(delivery)}`.toLowerCase().includes(search.toLowerCase()))
    .sort((a, b) => sort === 'oldest' ? new Date(a.deliveredAt) - new Date(b.deliveredAt) : new Date(b.deliveredAt) - new Date(a.deliveredAt)),
  [deliveries, search, status, sort])

  return (
    <section className="delivery-page">
      <PageHeader title="Delivery list" description="Search, filter, sort, and open any persisted delivery record." />
      <Card>
        <div className="list-filters">
          <TextInput label="Search" id="delivery-search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Reference, PO, or supplier" />
          <SelectInput label="Status" id="delivery-status" value={status} onChange={(e) => setStatus(e.target.value)} options={['All', ...Object.keys(STATUS_TONE)].map((value) => ({ value, label: titleCase(value) }))} />
          <SelectInput label="Sort" id="delivery-sort" value={sort} onChange={(e) => setSort(e.target.value)} options={[{ value: 'newest', label: 'Newest first' }, { value: 'oldest', label: 'Oldest first' }]} />
        </div>
        {!filtered.length ? <EmptyState title="No matching deliveries" message="Change the filters or record a new delivery." /> : (
          <div className="table-wrap delivery-table-wrap">
            <table className="data-table">
              <thead><tr><th>Delivery</th><th>PO</th><th>Supplier</th><th>Received</th><th>Status</th><th>Action</th></tr></thead>
              <tbody>{filtered.map((delivery) => (
                <tr key={delivery.id}>
                  <td><strong>{delivery.deliveryReference}</strong><small className="table-subtext">Delivery #{delivery.id}</small></td>
                  <td>PO-{delivery.purchaseOrderId}</td><td>{deliverySupplier(delivery)}</td><td>{formatDate(delivery.deliveredAt)}</td><td><DeliveryStatus status={delivery.status} /></td>
                  <td><Button variant="secondary" className="table-action" onClick={() => onOpen(delivery.id)}>View details</Button></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
        <div className="pagination"><span>Showing {filtered.length} of {deliveries.length} deliveries</span><span>Page 1 of 1</span></div>
      </Card>
    </section>
  )
}

// ------------------------------------------------------------------ Form

function ReceiveDeliveryForm({ orders, userName, onCancel, onRecorded }) {
  const [form, setForm] = useState(EMPTY_FORM)
  const [lines, setLines] = useState({})
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState(null)
  const selectedOrder = orders.find((po) => String(po.id) === form.purchaseOrderId)
  function update(field, value) { setForm((current) => ({ ...current, [field]: value })) }
  function updateLine(materialId, field, value) { setLines((current) => ({ ...current, [materialId]: { ...current[materialId], [field]: value } })) }

  async function handleSubmit(event) {
    event.preventDefault()
    setSubmitError(null)
    if (!selectedOrder?.items?.length) { setSubmitError('Select a confirmed purchase order with at least one line item.'); return }
    const items = selectedOrder.items.map((item) => ({ materialId: item.materialId, receivedQuantity: Number(lines[item.materialId]?.receivedQuantity ?? 0), damagedQuantity: Number(lines[item.materialId]?.damagedQuantity ?? 0) }))
    if (items.some((item) => item.receivedQuantity < 0 || item.damagedQuantity < 0 || item.damagedQuantity > item.receivedQuantity)) {
      setSubmitError('Received and damaged quantities cannot be negative, and damaged cannot exceed received.')
      return
    }
    if (!form.deliveryReference.trim()) { setSubmitError('Enter the supplier delivery reference or invoice number.'); return }
    setSubmitting(true)
    try {
      const created = await qualityApi.recordDelivery({ purchaseOrderId: selectedOrder.id, deliveryReference: form.deliveryReference.trim(), items })
      setForm(EMPTY_FORM); setLines({}); onRecorded(created)
    } catch (err) { setSubmitError(err.message) } finally { setSubmitting(false) }
  }

  return (
    <section className="delivery-page">
      <PageHeader eyebrow={`RECEIVED BY ${initials(userName)}`} title="Record a delivery" description="Receive materials against a confirmed purchase order. Every line is validated by the backend." />
      {submitError && <ErrorState title="Delivery could not be recorded" message={submitError} />}
      <form onSubmit={handleSubmit} className="delivery-form">
        <Card>
          <div className="form-grid">
            <SelectInput label="Confirmed purchase order" id="purchaseOrderId" required value={form.purchaseOrderId} onChange={(e) => update('purchaseOrderId', e.target.value)} options={[{ value: '', label: 'Select a purchase order' }, ...orders.map((po) => ({ value: po.id, label: `PO-${po.id} · ${po.items?.length ?? 0} line(s)` }))]} />
            <TextInput label="Delivery reference / invoice" id="deliveryReference" required value={form.deliveryReference} onChange={(e) => update('deliveryReference', e.target.value)} placeholder="e.g. INV-9081" />
          </div>
        </Card>
        <Card title="Received line items" subtitle="Enter actual site quantities for the selected order.">
          {!selectedOrder ? <EmptyState title="Select a purchase order" message="Its materials and ordered quantities will appear here." /> : (
            <div className="receiving-lines">{selectedOrder.items.map((item) => (
              <div className="receiving-line" key={item.id ?? item.materialId}>
                <div><strong>{item.material?.name ?? `Material #${item.materialId}`}</strong><small>Ordered: {formatNumber(item.orderedQuantity)} {item.material?.unit ?? 'units'}</small></div>
                <TextInput id={`received-${item.materialId}`} label="Received" type="number" min="0" step="0.01" value={lines[item.materialId]?.receivedQuantity ?? ''} onChange={(e) => updateLine(item.materialId, 'receivedQuantity', e.target.value)} />
                <TextInput id={`damaged-${item.materialId}`} label="Damaged" type="number" min="0" step="0.01" value={lines[item.materialId]?.damagedQuantity ?? ''} onChange={(e) => updateLine(item.materialId, 'damagedQuantity', e.target.value)} />
              </div>
            ))}</div>
          )}
          <div className="form-actions"><Button variant="secondary" onClick={onCancel} disabled={submitting}>Cancel</Button><Button type="submit" disabled={submitting || !selectedOrder}>{submitting ? 'Recording…' : 'Submit delivery entry'}</Button></div>
        </Card>
      </form>
    </section>
  )
}

// ------------------------------------------------------------------ Detail

function DeliveryDetail({ delivery, deliveries, onOpen }) {
  if (!delivery) return <section className="delivery-page"><PageHeader title="Delivery detail" description="Select a persisted delivery to inspect its receiving information." /><Card><EmptyState title="No delivery selected" message="Open a record from the delivery list." /></Card></section>
  const received = delivery.items?.reduce((sum, item) => sum + Number(item.receivedQuantity ?? 0), 0) ?? 0
  const damaged = delivery.items?.reduce((sum, item) => sum + Number(item.damagedQuantity ?? 0), 0) ?? 0
  return (
    <section className="delivery-page">
      <PageHeader
        eyebrow={`DELIVERY #${delivery.id}`}
        title={delivery.deliveryReference}
        description="Reusable detail view for receiving information and activity history."
        actions={<DeliveryStatus status={delivery.status} />}
      />
      <div className="detail-columns">
        <Card title="Information" subtitle="Persisted receiving and purchase-order details">
          <div className="detail-row"><span>Reference</span><strong>{delivery.deliveryReference}</strong></div>
          <div className="detail-row"><span>Purchase order</span><strong>PO-{delivery.purchaseOrderId}</strong></div>
          <div className="detail-row"><span>Supplier</span><strong>{deliverySupplier(delivery)}</strong></div>
          <div className="detail-row"><span>Received at</span><strong>{formatDate(delivery.deliveredAt, true)}</strong></div>
          <div className="detail-row"><span>Received quantity</span><strong>{formatNumber(received)}</strong></div>
          <div className="detail-row"><span>Damaged quantity</span><strong>{formatNumber(damaged)}</strong></div>
        </Card>
        <Card title="Activity & history" subtitle="Traceable Component 3 timeline">
          <ul className="activity-list">
            <li className="activity-item"><span className="activity-dot" /><div><strong>Delivery recorded</strong><div className="activity-time">{formatDate(delivery.deliveredAt, true)}</div></div></li>
            <li className="activity-item"><span className="activity-dot" /><div><strong>Discrepancy analysis completed</strong><div className="activity-time">Delivery workflow persisted by ASP.NET Core</div></div></li>
            <li className="activity-item"><span className="activity-dot" /><div><strong>Status set to {titleCase(delivery.status)}</strong><div className="activity-time">Based on ordered, received, and damaged quantities</div></div></li>
          </ul>
          <div className="detail-items">
            <h3>Line items</h3>
            {delivery.items?.map((item) => <div className="detail-line" key={item.id ?? item.materialId}><div><strong>{item.material?.name ?? `Material #${item.materialId}`}</strong><small>Received {formatNumber(item.receivedQuantity)} · damaged {formatNumber(item.damagedQuantity)}</small></div><StatusBadge status={Number(item.damagedQuantity) > 0 ? 'warning' : 'success'}>{Number(item.damagedQuantity) > 0 ? 'Review' : 'Matched'}</StatusBadge></div>)}
          </div>
        </Card>
      </div>
      {deliveries.length > 1 && <div className="detail-picker"><span>Open another delivery</span><SelectInput id="detail-delivery" label="" value={delivery.id} onChange={(e) => onOpen(Number(e.target.value))} options={deliveries.map((item) => ({ value: item.id, label: `#${item.id} · ${item.deliveryReference}` }))} /></div>}
    </section>
  )
}

// ------------------------------------------------------------------ UI states

function DeliveryUiStates({ onAction }) {
  return (
    <section className="delivery-page">
      <PageHeader title="UI states" description="Shared feedback, loading, and status components used across Component 3." />
      <div className="states-grid">
        <Card><LoadingState message="Loading delivery information…" /></Card>
        <Card><EmptyState title="Nothing here yet" message="New delivery records will appear here." actionLabel="Record delivery" onAction={onAction} /></Card>
        <Card><ErrorState message="The delivery service is temporarily unavailable." onRetry={() => {}} /></Card>
        <Card title="Status styles" subtitle="Consistent meaning across every delivery view.">
          <div className="badge-preview"><StatusBadge status="success">Received</StatusBadge><StatusBadge status="warning">Partially received</StatusBadge><StatusBadge status="danger">Discrepancy reported</StatusBadge><StatusBadge status="info">In transit</StatusBadge></div>
          <div className="button-preview"><Button>Primary action</Button><Button variant="secondary">Secondary</Button><Button variant="danger">Danger</Button></div>
        </Card>
      </div>
    </section>
  )
}

function ReadOnlyNotice({ title, message }) {
  return <section className="delivery-page"><PageHeader title="Component 3" description="Role-aware delivery workspace." /><Card><EmptyState title={title} message={message} /></Card></section>
}

import { useEffect, useMemo, useState } from 'react'
import {
  Button,
  Card,
  Drawer,
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
  const [deliveries, setDeliveries] = useState([])
  const [purchaseOrders, setPurchaseOrders] = useState([])
  const [selectedId, setSelectedId] = useState(null)
  // Both panels are opt-in. The record form and the detail view are no longer
  // permanent page sections, so the list stays the primary content.
  const [isRecordOpen, setIsRecordOpen] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const canReceive = hasRole('SiteOfficer') || hasRole('ReceivingOfficer') || hasRole('Administrator')

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

  // Opening a detail no longer auto-selects a default delivery: with a drawer
  // there is no "first row" on screen to preselect, so nothing is shown until the
  // user actually asks for a delivery.
  function openDetail(id) { setSelectedId(id) }
  function closeDetail() { setSelectedId(null) }

  if (loading) return <LoadingState message="Loading deliveries…" />
  if (error) return <ErrorState title="Could not load deliveries" message={error} onRetry={loadWorkspace} />

  return (
    <div className="delivery-workspace">
      <PageHeader
        title="Deliveries"
        description="Monitor receiving, discrepancies and delivery risks."
        actions={canReceive
          ? <Button onClick={() => setIsRecordOpen(true)}>+ Record delivery</Button>
          : <StatusBadge status="neutral">View only</StatusBadge>}
      />

      <DeliveryStats deliveries={deliveries} orders={purchaseOrders} />

      <WorkflowOverview deliveries={deliveries} orders={purchaseOrders} />

      <DeliveryList deliveries={deliveries} onOpen={openDetail} />

      <Drawer
        open={isRecordOpen}
        title="Record a delivery"
        subtitle="Receive materials against a confirmed purchase order. Every line is validated by the backend."
        onClose={() => setIsRecordOpen(false)}
      >
        <ReceiveDeliveryForm
          orders={purchaseOrders}
          userName={user?.fullName}
          onRecorded={(created) => {
            setIsRecordOpen(false)
            loadWorkspace()
            setSelectedId(created.id)
          }}
        />
      </Drawer>

      <Drawer
        open={selectedDelivery != null}
        title={selectedDelivery ? `Delivery #${selectedDelivery.id}` : ''}
        subtitle={selectedDelivery?.deliveryReference ?? ''}
        onClose={closeDetail}
      >
        {selectedDelivery && <DeliveryDetail delivery={selectedDelivery} />}
      </Drawer>
    </div>
  )
}

// ------------------------------------------------------------------ Summary
// Compact counters plus a one-line workflow bar. This replaced a full dashboard
// section that repeated the delivery table (a "Recent activity" list and a
// "Latest receiving" list) immediately above that table.

function DeliveryStats({ deliveries, orders }) {
  const today = new Date().toDateString()
  const attention = deliveries.filter((item) => item.status === 'DiscrepancyReported').length
  const completedToday = deliveries.filter((item) => new Date(item.deliveredAt).toDateString() === today).length
  const completedPoIds = new Set(deliveries.map((item) => item.purchaseOrderId))
  const awaiting = orders.filter((po) => !completedPoIds.has(po.id)).length

  return (
    <div className="metric-grid">
      <Metric label="Awaiting" value={awaiting} tone="orange" />
      <Metric label="Records" value={deliveries.length} tone="blue" />
      <Metric label="Issues" value={attention} tone="red" />
      <Metric label="Today" value={completedToday} tone="green" />
    </div>
  )
}

function WorkflowOverview({ deliveries, orders }) {
  const received = deliveries.filter((item) => item.status === 'Received').length
  const attention = deliveries.filter((item) => item.status === 'DiscrepancyReported').length
  const completedPoIds = new Set(deliveries.map((item) => item.purchaseOrderId))
  const awaiting = orders.filter((po) => !completedPoIds.has(po.id)).length

  return (
    <Card title="Workflow" subtitle="Calculated from current delivery records">
      <Progress label="Received" value={deliveries.length ? Math.round((received / deliveries.length) * 100) : 0} tone="blue" />
      <Progress label="Discrepancy" value={deliveries.length ? Math.round((attention / deliveries.length) * 100) : 0} tone={attention ? 'orange' : 'green'} />
      <Progress label="Awaiting" value={orders.length ? Math.round((awaiting / orders.length) * 100) : 0} tone="blue" />
    </Card>
  )
}

function Metric({ label, value, tone }) {
  return <Card className={`metric-card metric-card--${tone}`}><span>{label}</span><strong>{value}</strong></Card>
}

function Progress({ label, value, tone }) {
  return <div className="delivery-progress"><div><span>{label}</span><StatusBadge status={tone === 'orange' ? 'warning' : tone === 'green' ? 'success' : 'info'}>{value}%</StatusBadge></div><div className="delivery-progress__track"><i style={{ width: `${Math.min(value, 100)}%` }} /></div></div>
}

function DeliveryStatus({ status }) {
  return <StatusBadge status={STATUS_TONE[status] ?? 'neutral'}>{titleCase(status)}</StatusBadge>
}


// The receiving DTO (DeliveriesController.Project) returns a flat supplierName
// resolved server-side. It previously read a nested purchaseOrder.supplier.name
// that the DTO never contained, so every row rendered the "Supplier record not
// expanded" fallback even though the API was sending the real name.
function deliverySupplier(delivery) {
  return delivery.supplierName
    ?? delivery.purchaseOrder?.supplierName
    ?? delivery.purchaseOrder?.supplier?.name
    ?? 'Supplier not recorded'
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

function ReceiveDeliveryForm({ orders, userName, onRecorded }) {
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
      {/* The drawer supplies the title; only the recorder attribution is
          specific to this form. */}
      {userName && <p className="delivery-recorder">Received by {initials(userName)}</p>}
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
                <div><strong>{item.materialName ?? `Material #${item.materialId}`}</strong><small>Ordered: {formatNumber(item.orderedQuantity)} {item.unit ?? 'units'}</small></div>
                {/* max mirrors the backend rule Received <= Ordered, so an
                    over-receipt is caught in the browser as well as server-side. */}
                <TextInput id={`received-${item.materialId}`} label="Received" type="number" min="0" max={item.orderedQuantity} step="0.01" value={lines[item.materialId]?.receivedQuantity ?? ''} onChange={(e) => updateLine(item.materialId, 'receivedQuantity', e.target.value)} />
                <TextInput id={`damaged-${item.materialId}`} label="Damaged" type="number" min="0" step="0.01" value={lines[item.materialId]?.damagedQuantity ?? ''} onChange={(e) => updateLine(item.materialId, 'damagedQuantity', e.target.value)} />
              </div>
            ))}</div>
          )}
          {/* The form is no longer a separate view, so "Cancel" used to mean
              "leave this tab". It now clears the in-progress draft, which is
              what the button still means to someone filling the form in. */}
          <div className="form-actions"><Button variant="secondary" onClick={() => { setForm(EMPTY_FORM); setLines({}); setSubmitError(null) }} disabled={submitting}>Clear form</Button><Button type="submit" disabled={submitting || !selectedOrder}>{submitting ? 'Recording…' : 'Submit delivery entry'}</Button></div>
        </Card>
      </form>
    </section>
  )
}

// ------------------------------------------------------------------ Detail

function DeliveryDetail({ delivery }) {
  // COMPONENT 3 agent result. Held here (not in the page shell) so it resets
  // whenever a different delivery is selected, instead of leaving one
  // delivery's analysis on screen under another's details.
  const [analysis, setAnalysis] = useState(null)
  const [analyzing, setAnalyzing] = useState(false)
  const [analysisError, setAnalysisError] = useState(null)

  useEffect(() => {
    setAnalysis(null)
    setAnalysisError(null)
  }, [delivery?.id])

  async function runDiscrepancyAnalysis() {
    setAnalyzing(true)
    setAnalysisError(null)
    try {
      setAnalysis(await qualityApi.analyzeDeliveryDiscrepancy(delivery.id))
    } catch (err) {
      setAnalysisError(err.message)
    } finally {
      setAnalyzing(false)
    }
  }

  // The drawer only mounts this component with a resolved delivery, so the old
  // "No delivery selected / select a record" panel is unreachable now.
  if (!delivery) return null
  const received = delivery.items?.reduce((sum, item) => sum + Number(item.receivedQuantity ?? 0), 0) ?? 0
  const damaged = delivery.items?.reduce((sum, item) => sum + Number(item.damagedQuantity ?? 0), 0) ?? 0
  // Ordered comes from the linked purchase order, resolved server-side, so a
  // receiver sees the full ordered/received/damaged/shortage picture without
  // cross-referencing the PO.
  const ordered = delivery.items?.reduce((sum, item) => sum + Number(item.orderedQuantity ?? 0), 0) ?? 0
  const shortage = Math.max(0, ordered - received)
  // Records created before the backend enforced Received <= Ordered can still
  // hold an impossible quantity. Showing "Shortage: None" next to a
  // "Discrepancy Reported" status is self-contradictory, so surface the
  // anomaly explicitly rather than silently showing "None".
  const overReceipt = received - ordered
  const hasOverReceipt = overReceipt > 0
  return (
    <section className="delivery-page">
      {/* The drawer supplies the title and reference; this only adds the status
          badge, which the drawer header has no room for. */}
      <div className="detail-status"><DeliveryStatus status={delivery.status} /></div>
      <div className="detail-columns">
        <Card title="Information" subtitle="Persisted receiving and purchase-order details">
          <div className="detail-row"><span>Reference</span><strong>{delivery.deliveryReference}</strong></div>
          <div className="detail-row"><span>Purchase order</span><strong>PO-{delivery.purchaseOrderId}</strong></div>
          <div className="detail-row"><span>Supplier</span><strong>{deliverySupplier(delivery)}</strong></div>
          <div className="detail-row"><span>Received at</span><strong>{formatDate(delivery.deliveredAt, true)}</strong></div>
          <div className="detail-row"><span>Ordered quantity</span><strong>{formatNumber(ordered)}</strong></div>
          <div className="detail-row"><span>Received quantity</span><strong>{formatNumber(received)}</strong></div>
          <div className="detail-row"><span>Damaged quantity</span><strong>{formatNumber(damaged)}</strong></div>
          <div className="detail-row"><span>Shortage</span><strong>{shortage > 0 ? formatNumber(shortage) : 'None'}</strong></div>
          {hasOverReceipt && (
            <div className="detail-row">
              <span>Over-receipt</span>
              <StatusBadge status="danger">
                {formatNumber(overReceipt)} more than ordered — this record predates quantity validation
              </StatusBadge>
            </div>
          )}
        </Card>
        <Card title="Activity & history" subtitle="Traceable delivery timeline">
          <ul className="activity-list">
            <li className="activity-item"><span className="activity-dot" /><div><strong>Delivery recorded</strong><div className="activity-time">{formatDate(delivery.deliveredAt, true)}</div></div></li>
            <li className="activity-item"><span className="activity-dot" /><div><strong>Discrepancy analysis {analysis ? 'completed' : 'not yet run in this session'}</strong><div className="activity-time">{analysis ? `${analysis.agent} · ${analysis.tool}` : 'Run the AI Delivery Risk Analysis below to see the agent result'}</div></div></li>
            <li className="activity-item"><span className="activity-dot" /><div><strong>Status set to {titleCase(delivery.status)}</strong><div className="activity-time">Based on ordered, received, and damaged quantities</div></div></li>
          </ul>
          <div className="detail-items">
            <h3>Line items</h3>
            {delivery.items?.map((item) => <div className="detail-line" key={item.id ?? item.materialId}><div><strong>{item.materialName ?? `Material #${item.materialId}`}</strong><small>Received {formatNumber(item.receivedQuantity)} · damaged {formatNumber(item.damagedQuantity)}</small></div><StatusBadge status={Number(item.damagedQuantity) > 0 ? 'warning' : 'success'}>{Number(item.damagedQuantity) > 0 ? 'Review' : 'Matched'}</StatusBadge></div>)}
          </div>
        </Card>
      </div>
      {/* COMPONENT 3 — AI Delivery Risk Analysis.
          This is the real DeliveryDiscrepancyAgent (:8003) result, surfaced so
          the agent contribution is demonstrable rather than asserted in copy.
          Advisory only: it never changes the recorded delivery status. */}
      <Card
        title="AI Delivery Risk Analysis"
        subtitle="Runs DeliveryDiscrepancyAgent (analyze_discrepancy) over this delivery. Advisory only."
      >
        <div className="form-actions">
          <Button variant="secondary" onClick={runDiscrepancyAnalysis} disabled={analyzing}>
            {analyzing ? 'Analyzing…' : analysis ? 'Re-run Analysis' : 'Run Delivery Analysis'}
          </Button>
        </div>

        {analysisError && (
          <div style={{ marginTop: '1rem' }}>
            <ErrorState title="Discrepancy analysis failed" message={analysisError} />
          </div>
        )}

        {analysis && (
          <div style={{ marginTop: '1rem' }}>
            <div className="detail-row"><span>Agent</span><strong>{analysis.agent}</strong></div>
            <div className="detail-row"><span>Tool</span><strong>{analysis.tool}</strong></div>
            <div className="detail-row"><span>Analysis</span><strong>{analysis.summary}</strong></div>
            <div className="detail-row"><span>Ordered</span><strong>{formatNumber(analysis.orderedQuantity)}</strong></div>
            <div className="detail-row"><span>Received</span><strong>{formatNumber(analysis.receivedQuantity)}</strong></div>
            <div className="detail-row">
              <span>Shortage</span>
              <strong>{analysis.shortageDetected ? `${formatNumber(analysis.shortageQuantity)} unit(s)` : 'None'}</strong>
            </div>
            <div className="detail-row">
              <span>Damaged</span>
              <strong>{analysis.damageDetected ? `${formatNumber(analysis.damagedQuantity)} unit(s)` : 'None'}</strong>
            </div>
            <div className="detail-row"><span>Risk</span><strong>Delivery quantity/quality discrepancy</strong></div>
            <div className="detail-row"><span>Recommendation</span><strong>{analysis.recommendation}</strong></div>
            <div className="detail-row">
              <span>Agent status</span>
              <StatusBadge status="success">Completed</StatusBadge>
            </div>
            <div className="detail-row">
              <span>Execution source</span>
              <StatusBadge status="info">{analysis.executionSource}</StatusBadge>
            </div>
            <div className="detail-row">
              <span>Recorded status</span>
              <DeliveryStatus status={delivery.status} />
            </div>
          </div>
        )}
      </Card>

    </section>
  )
}


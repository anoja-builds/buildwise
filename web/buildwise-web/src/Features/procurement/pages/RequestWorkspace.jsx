import { useEffect, useState } from 'react'
import { Button, Card, ErrorState, LoadingState, PageHeader, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import QuotationEntryForm from '../components/QuotationEntryForm'
import QuotationComparisonView from '../components/QuotationComparisonView'
import AIRecommendationReview from '../components/AIRecommendationReview'
import ProcurementApprovalPanel from '../components/ProcurementApprovalPanel'

const TABS = ['Quotations', 'Comparison & AI Recommendation']

export default function RequestWorkspace({ requestId, role, onBack, onViewPurchaseOrder }) {
  const [requestDetail, setRequestDetail] = useState(null)
  const [comparison, setComparison] = useState(null)
  const [workflow, setWorkflow] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [tab, setTab] = useState(TABS[0])
  const [runningAnalysis, setRunningAnalysis] = useState(false)
  const [deciding, setDeciding] = useState(false)
  const [creatingPo, setCreatingPo] = useState(false)
  const [notice, setNotice] = useState('')
  const [showQuotationForm, setShowQuotationForm] = useState(false)
  const [showComparison, setShowComparison] = useState(false)

  const loadCore = async () => {
    const [detail, compareData, latest] = await Promise.all([
      procurementApi.getMaterialRequest(requestId),
      procurementApi.compareQuotations(requestId),
      procurementApi.getLatestWorkflow(requestId)
    ])
    setRequestDetail(detail)
    setComparison(compareData)
    setWorkflow(latest)
  }

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      await loadCore()
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [requestId])

  const handleQuotationCreated = async () => {
    setNotice('Quotation recorded.')
    setShowQuotationForm(false)
    await loadCore()
  }

  const handleDeleteQuotation = async (id) => {
    try {
      await procurementApi.deleteQuotation(id)
      await loadCore()
    } catch (err) {
      setError(err.message)
    }
  }

  const handleRunAnalysis = async () => {
    setRunningAnalysis(true)
    setError(null)
    try {
      const { workflowId } = await procurementApi.startWorkflow(requestId)
      const details = await procurementApi.getWorkflow(workflowId)
      setWorkflow(details)
      setTab(TABS[1])
    } catch (err) {
      setError(err.message)
    } finally {
      setRunningAnalysis(false)
    }
  }

  const handleDecision = async (decision, comment) => {
    setDeciding(true)
    setError(null)
    try {
      const result = await procurementApi.recordDecision(workflow.id, decision, comment)
      const refreshed = await procurementApi.getWorkflow(workflow.id)
      setWorkflow(refreshed)
      if (decision === 'Approve') {
        setCreatingPo(true)
        const po = await procurementApi.getPurchaseOrder(result.purchaseOrderId)
        setNotice(`Purchase Order #${po.id} created for ${po.supplierName}.`)
        await loadCore()
        setCreatingPo(false)
        onViewPurchaseOrder?.(po.id)
      } else {
        setNotice(`Decision recorded: ${decision}.`)
      }
    } catch (err) {
      setError(err.message)
      setCreatingPo(false)
    } finally {
      setDeciding(false)
    }
  }

  if (loading) return <LoadingState message="Loading request workspace…" />
  if (error && !requestDetail) return <ErrorState message={error} onRetry={load} />
  if (!requestDetail) return null

  return (
    <div className="stack">
      <button type="button" className="proc-back" onClick={onBack}>← Back to approved requests</button>
      {tab === TABS[0] ? <Card><div className="workflow-bid"><div><span className="page-header__eyebrow">MATERIAL REQUEST #{requestDetail.id}</span><h1 className="workspace-project">{requestDetail.projectName}</h1><span className="muted">Required by {requestDetail.requiredDate}</span></div><StatusBadge status="success">{requestDetail.status}</StatusBadge></div></Card> : <PageHeader eyebrow={`MATERIAL REQUEST #${requestDetail.id}`} title={role === 'Manager' ? 'Procurement Manager Sign-off' : 'AI Procurement Recommendation'} description={requestDetail.projectName} />}

      {notice && <div className="proc-mock-banner" style={{ background: 'var(--color-success-100)', color: 'var(--color-success-700)', borderColor: 'var(--color-success-700)' }}>{notice}</div>}
      {error && requestDetail && <div className="proc-mock-banner" style={{ background: 'var(--color-danger-100)', color: 'var(--color-danger-700)', borderColor: 'var(--color-danger-700)' }}>{error}</div>}

      {tab === TABS[0] && <Card title="Requested Materials, Specifications & Quantities"><div className="table-wrap"><table className="data-table"><thead><tr><th>Material</th><th>Quantity</th><th>Notes / quality preferences</th></tr></thead><tbody>{requestDetail.items.map(item => <tr key={item.id}><td>{item.materialName}</td><td>{item.requestedQuantity} {item.unit}</td><td>{item.notes || 'Not recorded'}</td></tr>)}</tbody></table></div></Card>}
      <div className="workflow-tabs">
        {TABS.map((t) => <button key={t} type="button" aria-selected={tab === t} onClick={() => setTab(t)}>{t}</button>)}
      </div>

      {tab === TABS[0] && (
        <div className="stack">
          <div className="workflow-bid"><h3>Collected Supplier Bids</h3><div className="actions"><Button variant="secondary" onClick={() => setShowQuotationForm(v => !v)}>{showQuotationForm ? 'Close quotation form' : '+ Add Supplier Quote'}</Button><Button variant="secondary" onClick={() => setShowComparison(v => !v)}>{showComparison ? 'Hide comparison' : 'Compare Side-by-Side'}</Button><Button onClick={handleRunAnalysis} disabled={runningAnalysis || Boolean(workflow?.purchaseOrderId)}>{runningAnalysis ? 'Running AI analysis…' : 'Run AI Analysis'}</Button></div></div>
          {showQuotationForm && <QuotationEntryForm requestDetail={requestDetail} onCreated={handleQuotationCreated} />}
          <div className="workflow-bids">{comparison?.quotations.map(q => <Card key={q.id}><div className="workflow-bid"><div><h3>Quotation #{q.id} · {q.supplierName}</h3><span className="muted">Valid until {q.validUntil}</span></div><strong className="bid-total">{q.totalAmount.toLocaleString()}</strong><StatusBadge status="info">{q.status}</StatusBadge></div></Card>)}</div>
          {showComparison && <QuotationComparisonView comparison={comparison} onDeleteQuotation={handleDeleteQuotation} onRunAnalysis={() => { setTab(TABS[1]); handleRunAnalysis() }} running={runningAnalysis} />}
        </div>
      )}

      {tab === TABS[1] && (
        <div className="stack">
            <div className="actions">
              <Button onClick={handleRunAnalysis} disabled={runningAnalysis || Boolean(workflow?.purchaseOrderId)}>{runningAnalysis ? 'Running AI analysis…' : workflow ? 'Re-run AI Analysis' : 'Run AI Analysis'}</Button>
              {creatingPo && <span className="muted">Creating purchase order…</span>}
              <Button variant="secondary" onClick={load}>Refresh workflow</Button>
          {workflow?.purchaseOrderId && <Button onClick={() => onViewPurchaseOrder?.(workflow.purchaseOrderId)}>View Purchase Order #{workflow.purchaseOrderId}</Button>}
          </div><div className={`workflow-columns ${role === 'Manager' ? 'manager-review' : ''}`}><AIRecommendationReview workflow={workflow} />
          {workflow && <aside className="workflow-summary"><ProcurementApprovalPanel workflow={workflow} role={role} onDecide={handleDecision} deciding={deciding || creatingPo} /></aside>}</div>
        </div>
      )}
    </div>
  )
}

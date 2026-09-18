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

  const loadCore = async () => {
    const [detail, compareData] = await Promise.all([
      procurementApi.getMaterialRequest(requestId),
      procurementApi.compareQuotations(requestId)
    ])
    setRequestDetail(detail)
    setComparison(compareData)
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
      await procurementApi.recordDecision(workflow.id, decision, comment)
      const refreshed = await procurementApi.getWorkflow(workflow.id)
      setWorkflow(refreshed)
      if (decision === 'Approve') {
        setCreatingPo(true)
        const po = await procurementApi.createPurchaseOrderFromWorkflow(workflow.id)
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
      <PageHeader eyebrow={`MATERIAL REQUEST #${requestDetail.id}`} title={requestDetail.projectName} description={requestDetail.reason || 'Quote, compare, and approve procurement for this request.'} />
      <div className="actions"><StatusBadge status="success">{requestDetail.status}</StatusBadge><span className="muted">Required by {requestDetail.requiredDate}</span></div>

      {notice && <div className="proc-mock-banner" style={{ background: 'var(--color-success-100)', color: 'var(--color-success-700)', borderColor: 'var(--color-success-700)' }}>{notice}</div>}
      {error && requestDetail && <div className="proc-mock-banner" style={{ background: 'var(--color-danger-100)', color: 'var(--color-danger-700)', borderColor: 'var(--color-danger-700)' }}>{error}</div>}

      <div className="proc-tabs">
        {TABS.map((t) => <button key={t} type="button" className={`proc-tab ${tab === t ? 'proc-tab--active' : ''}`} onClick={() => setTab(t)}>{t}</button>)}
      </div>

      {tab === TABS[0] && (
        <div className="stack">
          <QuotationEntryForm requestDetail={requestDetail} onCreated={handleQuotationCreated} />
          <QuotationComparisonView comparison={comparison} onDeleteQuotation={handleDeleteQuotation} onRunAnalysis={() => { setTab(TABS[1]); handleRunAnalysis() }} running={runningAnalysis} />
        </div>
      )}

      {tab === TABS[1] && (
        <div className="stack">
          <Card>
            <div className="actions">
              <Button onClick={handleRunAnalysis} disabled={runningAnalysis}>{runningAnalysis ? 'Running AI analysis…' : workflow ? 'Re-run AI Analysis' : 'Run AI Analysis'}</Button>
              {creatingPo && <span className="muted">Creating purchase order…</span>}
            </div>
          </Card>
          <AIRecommendationReview workflow={workflow} />
          {workflow && <ProcurementApprovalPanel workflow={workflow} role={role} onDecide={handleDecision} deciding={deciding || creatingPo} />}
        </div>
      )}
    </div>
  )
}

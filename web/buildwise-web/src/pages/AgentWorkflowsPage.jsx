import { useEffect, useState } from 'react'
import {
  Card,
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeader,
  Pagination,
  SelectInput,
  StatusBadge,
} from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import './common/common.css'

const PAGE_SIZE = 10

export default function AgentWorkflowsPage() {
  const [workflows, setWorkflows] = useState([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState('all')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState(null)
  const [detailLoading, setDetailLoading] = useState(false)

  async function load(nextPage = page) {
    setLoading(true)
    setError('')
    setSelected(null)
    try {
      const result = await qualityApi.listAgentWorkflows({
        page: nextPage,
        pageSize: PAGE_SIZE,
        status: status === 'all' ? undefined : status,
      })
      setWorkflows(result.items)
      setTotal(result.total)
      setPage(result.page)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    setSelected(null)
    qualityApi.listAgentWorkflows({
      page: 1,
      pageSize: PAGE_SIZE,
      status: status === 'all' ? undefined : status,
    }).then((result) => {
      if (cancelled) return
      setWorkflows(result.items)
      setTotal(result.total)
      setPage(result.page)
    }).catch((err) => {
      if (!cancelled) setError(err.message)
    }).finally(() => {
      if (!cancelled) setLoading(false)
    })
    return () => { cancelled = true }
  }, [status])

  async function showDetails(id) {
    setDetailLoading(true)
    setError('')
    try {
      setSelected(await qualityApi.getAgentWorkflow(id))
    } catch (err) {
      setError(err.message)
    } finally {
      setDetailLoading(false)
    }
  }

  return (
    <div className="stack">
      <div className="toolbar">
        <PageHeader
          eyebrow="Agentic AI"
          title="Agent Workflows"
          description="Auditable objectives, ordered agent steps, structured validation and human approval outcomes. Stored summaries never include hidden model reasoning."
        />
        <SelectInput
          label="Status"
          id="workflow-status"
          value={status}
          onChange={(event) => setStatus(event.target.value)}
          options={[
            { value: 'all', label: 'All statuses' },
            { value: 'RevisionRequired', label: 'Revision required' },
            { value: 'Running', label: 'Running' },
            { value: 'AwaitingApproval', label: 'Awaiting approval' },
            { value: 'Completed', label: 'Completed' },
            { value: 'Failed', label: 'Failed' },
          ]}
        />
      </div>

      {loading && <LoadingState message="Loading agent execution history…" />}
      {error && <ErrorState message={error} onRetry={() => load()} />}
      {!loading && !error && workflows.length === 0 && (
        <EmptyState title="No agent workflows" message="Run a manager-triggered request analysis or a procurement workflow to create an audit record." />
      )}

      {!loading && !error && workflows.length > 0 && (
        <Card>
          <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>#</th><th>Objective</th><th>Status</th><th>Approval</th><th>Steps</th><th>Created</th><th /></tr></thead>
              <tbody>
                {workflows.map((workflow) => (
                  <tr key={workflow.id}>
                    <td>{workflow.id}</td>
                    <td>{workflow.objective}</td>
                    <td><StatusBadge tone={statusTone(workflow.status)}>{workflow.status}</StatusBadge></td>
                    <td>{workflow.approvalStatus}</td>
                    <td>{workflow.stepCount} ({workflow.failedStepCount} failed)</td>
                    <td>{new Date(workflow.createdAt).toLocaleString()}</td>
                    <td><button className="table-action" type="button" onClick={() => showDetails(workflow.id)}>History</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={load} />
        </Card>
      )}

      {detailLoading && <LoadingState message="Loading execution summary…" />}
      {selected && !detailLoading && (
        <Card title={`Workflow #${selected.workflow.id} execution summary`} subtitle={selected.workflow.finalOutcome ?? ''}>
          <div className="stack">
            {selected.steps.map((step) => (
              <details className="summary-card" key={step.id}>
                <summary>{step.stepOrder}. {step.agentRole} — {step.stepName} ({step.status})</summary>
                <div className="detail-row"><span className="detail-row__label">Timings</span><span className="detail-row__value">{formatTime(step.startedAt)} → {formatTime(step.completedAt)}</span></div>
                <div className="detail-row"><span className="detail-row__label">Structured result</span><pre className="workflow-json">{pretty(step.structuredResult)}</pre></div>
                <div className="detail-row"><span className="detail-row__label">Validation</span><pre className="workflow-json">{pretty(step.validationResult)}</pre></div>
                {step.errorMessage && <div className="field__error">{step.errorMessage}</div>}
              </details>
            ))}
            {selected.approvals.length > 0 && (
              <div><h3>Human approval</h3>{selected.approvals.map((approval) => <div className="detail-row" key={approval.id}><span className="detail-row__label">{approval.decision}</span><span className="detail-row__value">{approval.comment || 'No comment'} — {formatTime(approval.decisionDate)}</span></div>)}</div>
            )}
          </div>
        </Card>
      )}
    </div>
  )
}

function statusTone(status) {
  if (status === 'Completed') return 'success'
  if (status === 'Failed') return 'danger'
  if (status === 'RevisionRequired') return 'warning'
  if (status === 'AwaitingApproval' || status === 'Running') return 'warning'
  return 'neutral'
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString() : 'Not recorded'
}

function pretty(value) {
  if (!value) return 'Not recorded'
  try { return JSON.stringify(JSON.parse(value), null, 2) } catch { return value }
}

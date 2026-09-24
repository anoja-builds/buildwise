import { useEffect, useRef, useState } from 'react'
import { Button, Card, ErrorState, LoadingState, StatusBadge, TextInput } from '../../components/shared'
import { qualityApi } from './services/qualityApi'

export default function QualityRiskPanel({ inspection }) {
  const [workflow, setWorkflow] = useState(null)
  const [workflowId, setWorkflowId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const locked = useRef(false)
  const active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])

  async function run(operation) {
    if (locked.current) return
    locked.current = true; setBusy(true); setError(''); setWorkflow(null)
    try {
      const result = await operation()
      if (!active.current) return
      if (result.inspectionId !== inspection.id) throw new Error('This workflow belongs to a different inspection. Open that inspection to review it.')
      setWorkflow(result); setWorkflowId(String(result.workflowId))
    } catch (err) {
      if (!active.current) return
      // Analyse returns HTTP 502 with the persisted failed workflow, not ProblemDetails.
      if (err.status === 502 && err.data?.status === 'Failed' && err.data?.inspectionId === inspection.id && Array.isArray(err.data.steps)) {
        setWorkflow(err.data); setWorkflowId(String(err.data.workflowId))
      } else setError(err.message)
    } finally {
      locked.current = false
      if (active.current) setBusy(false)
    }
  }

  const analysis = workflow?.steps?.find((step) => step.structuredResult?.recommendation)?.structuredResult
  const recommendation = analysis?.recommendation
  const validated = workflow?.status === 'Completed' && workflow.steps.some((step) => step.status === 'Completed' && step.validationResult?.valid === true && step.validationResult?.advisoryOnly === true)

  return <Card title="Quality Risk Agent" subtitle="Advisory analysis only. An authorized human must separately create an NCR or record corrective action.">
    {inspection.status === 'Completed' ? <Button disabled={busy} onClick={() => run(() => qualityApi.analyseInspection(inspection.id))}>Analyse inspection</Button>
      : <p>Analysis is available after the inspection is completed.</p>}
    <form className="quality-actions" onSubmit={(event) => {
      event.preventDefault()
      const id = Number(workflowId)
      if (!Number.isSafeInteger(id) || id <= 0) { setError('Enter a positive workflow ID.'); return }
      run(() => qualityApi.getWorkflow(id))
    }}>
      <TextInput name="workflow-id" label="Workflow ID" type="number" min="1" step="1" required value={workflowId} onChange={(event) => setWorkflowId(event.target.value)} disabled={busy} hint="Use an ID from a previous analysis to retrieve its saved results." />
      <Button type="submit" variant="secondary" disabled={busy}>Load workflow</Button>
    </form>
    {busy && <LoadingState message="Waiting for the quality-agent API..." />}
    {error && <ErrorState message={error} />}
    {workflow && <div className="stack" aria-live="polite">
      <h3>Workflow #{workflow.workflowId} <StatusBadge status={workflow.status === 'Failed' ? 'danger' : 'neutral'}>{workflow.status}</StatusBadge></h3>
      <p>{workflow.finalOutcome || 'No final outcome recorded.'}</p>
      <p>Approval state: {workflow.approvalStatus}. Quality recommendations do not have an approval action in this API.</p>
      {workflow.status === 'Failed' && <ErrorState title="Analysis failed" message="Review the execution steps below. No successful recommendation is confirmed." />}
      {recommendation && validated ? <section aria-label="Validated advisory recommendation">
        <h3>Risk: {recommendation.riskLevel}</h3><p>{recommendation.evidenceSummary}</p><p>{recommendation.rationaleSummary}</p>
        <p>NCR recommended: {recommendation.ncrRecommended ? 'Yes' : 'No'}</p>
        <h4>Risk flags</h4>
        {recommendation.riskFlags.length ? <ul>{recommendation.riskFlags.map((flag, index) => <li key={index}>{flag.flag}<p className="muted">Evidence: {flag.evidenceReferences.join(', ')}</p></li>)}</ul> : <p>No risk flags returned.</p>}
        <h4>Item recommendations</h4>
        {recommendation.itemRecommendations.length ? recommendation.itemRecommendations.map((item) => <section key={item.inspectionItemId}>
          <h4>Inspection item #{item.inspectionItemId}</h4>
          <dl className="quality-facts"><dt>NCR recommended</dt><dd>{item.ncrRecommended ? 'Yes' : 'No'}</dd><dt>Suggested severity</dt><dd>{item.suggestedSeverity || 'None'}</dd>
            <dt>Suggested issue</dt><dd>{item.suggestedIssueDescription || 'None'}</dd><dt>Suggested corrective action</dt><dd>{item.suggestedCorrectiveAction || 'None'}</dd>
            <dt>Rationale</dt><dd>{item.rationale}</dd><dt>Evidence</dt><dd>{item.evidenceReferences.join(', ')}</dd></dl>
        </section>) : <p>No item recommendations returned.</p>}
        <p>To act on this advice, review the inspection item and use its Create NCR action, or edit an existing NCR in Non-Conformances.</p>
      </section> : <p>No validated advisory recommendation is available. Any structured output below is audit evidence only.</p>}
      <section aria-label="Execution steps"><h3>Execution steps</h3>
        {[...workflow.steps].sort((a, b) => a.stepOrder - b.stepOrder).map((step) => <section key={step.stepOrder}>
          <h4>{step.stepOrder}. {step.stepName} — {step.status}</h4>
          {step.error && <p role="alert">{step.error}</p>}
          {step.validationResult && <div><h4>Validation results</h4><pre>{JSON.stringify(step.validationResult, null, 2)}</pre></div>}
          {step.structuredResult && <details><summary>Structured result and execution trace</summary><pre>{JSON.stringify(step.structuredResult, null, 2)}</pre></details>}
        </section>)}
      </section>
    </div>}
  </Card>
}

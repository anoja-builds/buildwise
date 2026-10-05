import { Card } from '../../../components/shared'

/**
 * The approval pipeline from the BuildWise scenario deck:
 *
 *   AI Recommendation → Deterministic Validation → Pending Manager Approval
 *     → Procurement Manager → Approve / Reject / Request Revision → Purchase Order
 *
 * Informational for every role — the decision buttons stay in
 * ProcurementApprovalPanel (manager-only). The point it makes for the demo:
 * an AI recommendation is advisory; only deterministic validation plus a
 * human manager decision can unlock the purchase order.
 */

// en-US keeps the demo figures stable ("LKR 1,100,000") regardless of the
// browser/Node locale, matching the numbers in the scenario deck.
const money = (n) => `LKR ${Number(n).toLocaleString('en-US')}`

const MARKER = { done: '✓', failed: '✕', active: '●', waiting: '○', locked: '○' }

export default function WorkflowPipeline({ workflow, budget = null }) {
  if (!workflow) return null

  const rec = workflow.recommendation
  const validation = workflow.validation
  const approval = workflow.approvalStatus ?? 'Pending'
  const hasWinner = Boolean(rec?.recommendedSupplierId)
  const decided = approval !== 'Pending'
  const valid = hasWinner && validation?.isValid === true

  const winnerAlt =
    rec?.rankedAlternatives?.find((a) => a.quotationId === rec.recommendedQuotationId) ??
    rec?.rankedAlternatives?.[0]
  const recommendedTotal = winnerAlt?.totalAmount ?? null
  const remaining =
    budget != null && recommendedTotal != null ? Number(budget) - Number(recommendedTotal) : null

  const stages = [
    {
      label: 'AI Recommendation',
      state: hasWinner ? 'done' : 'waiting',
      detail: hasWinner
        ? `${rec.recommendedSupplierName} — ${money(recommendedTotal ?? 0)} (lowest compliant landed cost).`
        : 'Run the AI analysis to rank the eligible quotations.',
    },
    {
      label: 'Deterministic Validation',
      state: !hasWinner ? 'locked' : !validation ? 'waiting' : validation.isValid ? 'done' : 'failed',
      detail: !hasWinner
        ? 'Runs automatically after the recommendation.'
        : !validation
          ? 'Re-checking the recommendation against the fixed business rules…'
          : validation.isValid
            ? 'Deterministic validation passed — eligibility, coverage, delivery and budget re-checked.'
            : 'Deterministic validation failed — the recommendation cannot proceed.',
    },
    {
      label: 'Pending Manager Approval',
      state: !valid ? 'locked' : decided ? 'done' : 'active',
      detail: !valid
        ? 'Unlocks once deterministic validation passes.'
        : decided
          ? `Resolved: ${approval}.`
          : 'Workflow is awaiting the Procurement Manager.',
    },
    {
      label: 'Procurement Manager',
      state: !valid ? 'locked' : decided ? 'done' : 'active',
      detail: !valid
        ? 'Waiting for a validated recommendation.'
        : decided
          ? 'Decision recorded by the Procurement Manager.'
          : 'Only the Procurement Manager (or Site Manager / Administrator) may decide — the officer cannot create the PO.',
    },
    {
      label: 'Approve / Reject / Request Revision',
      state: !valid
        ? 'locked'
        : !decided
          ? 'waiting'
          : approval === 'Approved'
            ? 'done'
            : approval === 'Rejected'
              ? 'failed'
              : 'active',
      detail: !decided
        ? 'Recorded from the Procurement Approval panel below.'
        : `Recorded decision: ${approval}.`,
    },
    {
      label: 'Purchase Order',
      state:
        workflow.purchaseOrderId != null
          ? 'done'
          : approval === 'Approved'
            ? 'active'
            : 'locked',
      detail:
        workflow.purchaseOrderId != null
          ? `Purchase Order #${workflow.purchaseOrderId} created.`
          : approval === 'Approved'
            ? 'Approved — creating the purchase order…'
            : 'Locked until the manager approves. The AI never creates a PO.',
    },
  ]

  return (
    <Card
      title="Approval pipeline"
      subtitle="AI recommendation → deterministic validation → human approval → purchase order. The AI advises; it never approves."
    >
      <ol className="pipeline">
        {stages.map((s) => (
          <li key={s.label} className={`pipeline__step pipeline__step--${s.state}`}>
            <span className="pipeline__marker" aria-hidden="true">{MARKER[s.state]}</span>
            <div>
              <div className="pipeline__label">{s.label}</div>
              <div className="pipeline__detail">{s.detail}</div>
            </div>
          </li>
        ))}
      </ol>

      {budget != null && (
        <div className="pipeline__budget" role="status">
          <span>
            Materials budget <strong>{money(budget)}</strong>
          </span>
          {recommendedTotal != null && (
            <span>
              Recommended total <strong>{money(recommendedTotal)}</strong>
            </span>
          )}
          {remaining != null && (
            <span className={remaining < 0 ? 'pipeline__budget-flag' : 'pipeline__budget-ok'}>
              {remaining < 0
                ? `Over budget by ${money(Math.abs(remaining))}`
                : `Remaining ${money(remaining)}`}
            </span>
          )}
        </div>
      )}
    </Card>
  )
}


import { useEffect, useState } from 'react'
import { Button, Card, LoadingState, ErrorState } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import { useAuth } from '../../../auth/AuthContext'
import { hasAnyRole, ROLES } from '../../../auth/accessControl'

/**
 * Step 5 — project materials budget.
 *
 * Reading is open to the procurement desk; editing is restricted to approvers
 * (Procurement Manager / Site Manager / Administrator), mirroring the API's
 * `ProcurementDecisionOnly` policy. The budget is the input to the deterministic
 * budget check that flags an over-budget recommendation before approval.
 */
export default function ProjectBudgetPanel({ projectId }) {
  const { roles } = useAuth()
  const canEdit = hasAnyRole(roles, [ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator])

  const [budget, setBudget] = useState(null)
  const [projectName, setProjectName] = useState('')
  const [draft, setDraft] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    let cancelled = false
    const load = async () => {
      setLoading(true)
      setError(null)
      try {
        const data = await procurementApi.getProjectBudget(projectId)
        if (cancelled) return
        setBudget(data.materialBudgetAmount)
        setProjectName(data.projectName ?? '')
        setDraft(data.materialBudgetAmount == null ? '' : String(data.materialBudgetAmount))
      } catch (err) {
        if (!cancelled) setError(err.message)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    load()
    return () => { cancelled = true }
  }, [projectId])

  const save = async (e) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    setSaved(false)
    try {
      // An empty field clears the allocation, which makes the budget check
      // not applicable rather than treating the project as zero-budget.
      const amount = draft.trim() === '' ? null : Number(draft)
      if (amount != null && (Number.isNaN(amount) || amount < 0)) {
        throw new Error('Enter a non-negative amount, or leave blank for no budget.')
      }
      const data = await procurementApi.updateProjectBudget(projectId, amount)
      setBudget(data.materialBudgetAmount)
      setSaved(true)
    } catch (err) {
      setError(err.message)
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <LoadingState message="Loading project budget…" />
  if (error && budget == null) return <ErrorState message={error} onRetry={() => window.location.reload()} />

  return (
    <Card
      title="Materials budget"
      subtitle={projectName ? `Project: ${projectName}` : undefined}
    >
      <form className="toolbar" onSubmit={save}>
        <div className="field">
          <label className="field__label" htmlFor="material-budget">Materials budget (LKR)</label>
          <input
            id="material-budget"
            className="field__control"
            type="number"
            min="0"
            step="0.01"
            value={draft}
            disabled={!canEdit}
            placeholder="No budget set"
            onChange={(e) => { setDraft(e.target.value); setSaved(false) }}
          />
          <span className="field__hint">
            {canEdit
              ? 'A quotation above this landed total is flagged for the approving manager. It is not blocked.'
              : 'Read-only. A Procurement Manager or Site Manager sets the allocation.'}
          </span>
        </div>
        {canEdit && (
          <Button type="submit" disabled={saving}>
            {saving ? 'Saving…' : 'Save budget'}
          </Button>
        )}
      </form>

      {saved && <p className="summary-card__note" role="status">Budget saved.</p>}
      {error && <p className="field__error" role="alert">{error}</p>}

      <p className="summary-card__note">
        {budget == null
          ? 'No budget recorded. The budget check does not apply to this project.'
          : `Currently allocated: LKR ${Number(budget).toLocaleString()}`}
      </p>
    </Card>
  )
}
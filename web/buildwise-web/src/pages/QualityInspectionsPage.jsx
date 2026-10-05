import { useEffect, useState } from 'react'
import { Button, Card, Drawer, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import QualityRiskPanel from '../Features/quality/components/QualityRiskPanel'
import QualityChecklist from '../Features/quality/components/QualityChecklist'

import './common/common.css'

/**
 * COMPONENT 4 (first half) — quality inspections.
 *
 * Split from the former single "Quality Inspections & Non-Conformance" page so
 * that inspecting a delivery and resolving a non-conformance become two
 * distinct jobs for two distinct audiences: a Quality Inspector records the
 * inspection, a Procurement Manager or Site Manager closes the NCR. The
 * lifecycle bar at the top states how an inspection connects back to the
 * delivery that produced it and forward to any NCR it spawns.
 */
export default function QualityInspectionsPage() {
  const [inspections, setInspections] = useState([])
  // COMPONENT 4 agent results, keyed by inspection id. Kept per inspection so
  // one analysis is never shown against a different inspection's row.
  const [analysisById, setAnalysisById] = useState({})
  const [activeAnalysis, setActiveAnalysis] = useState(null)
  const [analyzingId, setAnalyzingId] = useState(null)
  const [analysisError, setAnalysisError] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  /**
   * Runs the QualityRiskAnalysisAgent over one inspection. The result is
   * advisory: it recommends a risk level and corrective action, while the
   * inspection status and any NCRs remain the authoritative business records
   * created by the backend and the inspector.
   */
  async function runRiskAnalysis(inspectionId) {
    if (analysisById[inspectionId]) {
      setActiveAnalysis(analysisById[inspectionId])
      return
    }
    setAnalyzingId(inspectionId)
    setAnalysisError(null)
    try {
      const result = await qualityApi.analyzeQualityRisk(inspectionId)
      // Ignore a response for a different inspection rather than filing it
      // against the wrong row.
      if (result && result.inspectionId != null && result.inspectionId !== inspectionId) {
        setAnalysisError('The analysis response did not match this inspection. Please try again.')
        return
      }
      setAnalysisById((current) => ({ ...current, [inspectionId]: result }))
      setActiveAnalysis(result)
    } catch (err) {
      setAnalysisError(err.message)
    } finally {
      setAnalyzingId(null)
    }
  }

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setInspections(await qualityApi.listInspections())
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  if (loading) return <LoadingState message="Loading quality inspections…" />
  if (error) return <ErrorState title="Could not load inspections" message={error} onRetry={load} />

  const items = (i) => i.items ?? []
  const totalInspected = inspections.reduce((sum, i) => sum + items(i).reduce((s, it) => s + (it.inspectedQuantity ?? 0), 0), 0)
  const totalRejected = inspections.reduce((sum, i) => sum + items(i).reduce((s, it) => s + (it.rejectedQuantity ?? 0), 0), 0)
  const notFullyAccepted = inspections.filter((i) => i.overallDecision && i.overallDecision !== 'Accepted').length

  const summaryCards = [
    ['Inspections', inspections.length, 'Completed quality records', '#2563eb'],
    ['Not fully accepted', notFullyAccepted, 'Partial or rejected result', '#b45309'],
    ['Units inspected', totalInspected, 'Across all recorded lines', '#0f766e'],
    ['Units rejected', totalRejected, 'Each raises an NCR', '#b91c1c'],
  ]
  return (
    <div className="stack">
      <PageHeader
        title="Quality Inspections"
        description="Material accepted, partially accepted or rejected on site. Rejected lines automatically raise a non-conformance."
      />


      <div className="grid grid--4">
        {summaryCards.map(([label, value, note, color]) => (
          <Card key={label} className="summary-card" style={{ '--summary-color': color }}>
            <div className="summary-card__label">{label}</div>
            <div className="summary-card__value">{value}</div>
            <div className="summary-card__note">{note}</div>
          </Card>
        ))}
      </div>

      {analysisError && <div className="field__error" role="alert">{analysisError}</div>}

      <Card title="Inspection History" subtitle="Completed quality inspections linked to received deliveries.">
        {inspections.length === 0 ? (
          <EmptyState
            title="No inspections recorded"
            message="Inspections appear here once a Quality Inspector records a result against a received delivery."
          />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Inspection</th>
                  <th>Delivery</th>
                  <th>Criteria</th>
                  <th>Result</th>
                  <th>Checklist</th>
                  <th>Inspected</th>
                  <th>Recorded</th>
                  <th>AI</th>
                </tr>
              </thead>
              <tbody>
                {inspections.map((inspection) => {
                  const inspected = items(inspection).reduce((s, it) => s + (it.inspectedQuantity ?? 0), 0)
                  const rejected = items(inspection).reduce((s, it) => s + (it.rejectedQuantity ?? 0), 0)
                  return (
                    <tr key={inspection.id}>
                      <td><strong>INS-{inspection.id}</strong></td>
                      <td>DEL-{inspection.deliveryId}</td>
                      <td className="muted">{inspection.inspectionCriteria || '—'}</td>
                      <td>
                        <StatusBadge status={inspection.overallDecision === 'Accepted' ? 'success' : 'warning'}>
                          {inspection.overallDecision || '—'}
                        </StatusBadge>
                      </td>
                      <td><QualityChecklist inspection={inspection} /></td>
                      <td>
                        {inspected}
                        {rejected > 0 && <div className="muted">{rejected} rejected</div>}
                      </td>
                      <td>{inspection.inspectedAt ? new Date(inspection.inspectedAt).toLocaleDateString() : '—'}</td>
                      <td>
                        <Button
                          variant="secondary"
                          onClick={() => runRiskAnalysis(inspection.id)}
                          disabled={analyzingId === inspection.id}
                        >
                          {analyzingId === inspection.id ? 'Analysing…' : analysisById[inspection.id] ? 'View AI Risk' : 'Run AI Analysis'}
                        </Button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Drawer
        open={activeAnalysis != null}
        title={`AI Quality Risk — INS-${activeAnalysis?.inspectionId}`}
        subtitle="QualityRiskAnalysisAgent advisory risk assessment"
        onClose={() => setActiveAnalysis(null)}
      >
        {activeAnalysis && <QualityRiskPanel analysis={activeAnalysis} />}
      </Drawer>
    </div>
  )
}

/** One inspection row. Kept as a component so the <td> count stays stable. */
function FragmentRow({ children }) {
  return <tr>{children}</tr>
}

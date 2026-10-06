import { useEffect, useRef, useState } from 'react'
import { Button, Card, Drawer, EmptyState, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge, SuccessDialog, TextInput } from '../components/shared'
import { qualityApi } from '../services/qualityApi'
import QualityRiskPanel from '../Features/quality/components/QualityRiskPanel'
import QualityChecklist from '../Features/quality/components/QualityChecklist'
import { useAuth } from '../auth/AuthContext'
import { hasAnyRole, ROLES } from '../auth/accessControl'

import './common/common.css'

const INITIAL_INSPECTION_FORM = {
  deliveryId: '',
  materialId: 1,
  materialName: 'OPC Cement (50kg bag)',
  inspectedQuantity: 250,
  rejectedQuantity: 0,
  rejectionReason: '',
  quantityCheck: true,
  visualConditionCheck: true,
  moistureCheck: true,
  packagingCheck: true,
  defectsCheck: true,
  inspectionCriteria: 'Quantity, visual condition, moisture, and packaging verification per site QC protocol',
  observedResult: 'Material inspected and acceptable.',
  notes: '',
  evidenceFileName: '',
  evidenceFileUrl: '',
  evidenceFileSizeBytes: 0,
  evidenceContentType: 'image/jpeg',
}

/**
 * COMPONENT 4 (first half) — quality inspections.
 *
 * Split from the former single "Quality Inspections & Non-Conformance" page so
 * that inspecting a delivery and resolving a non-conformance become two
 * distinct jobs for two distinct audiences: a Quality Inspector records the
 * inspection, a Procurement Manager or Site Manager closes the NCR.
 */
export default function QualityInspectionsPage() {
  const { roles } = useAuth()
  const canInspect = hasAnyRole(roles, [ROLES.QualityInspector, ROLES.Administrator])

  const [inspections, setInspections] = useState([])
  const [analysisById, setAnalysisById] = useState({})
  const [activeAnalysis, setActiveAnalysis] = useState(null)
  const [analyzingId, setAnalyzingId] = useState(null)
  const [analysisError, setAnalysisError] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  // Record Inspection Form state
  const [isRecordOpen, setIsRecordOpen] = useState(false)
  const [deliveries, setDeliveries] = useState([])
  const [submittingInspection, setSubmittingInspection] = useState(false)
  const [recordError, setRecordError] = useState(null)
  const [recordNotice, setRecordNotice] = useState(null)
  const [previewImage, setPreviewImage] = useState(null)
  const [form, setForm] = useState(INITIAL_INSPECTION_FORM)

  const fileInputRef = useRef(null)
  const cameraInputRef = useRef(null)

  function handleImageFile(file) {
    if (!file) return
    if (file.size > 10_000_000) {
      setRecordError('Evidence files must be 10 MB or less.')
      return
    }
    const reader = new FileReader()
    reader.onload = (e) => {
      const dataUrl = e.target.result
      setPreviewImage(dataUrl)
      setForm((p) => ({
        ...p,
        evidenceFileName: file.name || `photo-${Date.now()}.jpg`,
        evidenceFileUrl: dataUrl,
        evidenceFileSizeBytes: file.size,
        evidenceContentType: file.type || 'image/jpeg',
      }))
    }
    reader.readAsDataURL(file)
  }

  async function runRiskAnalysis(inspectionId) {
    if (analysisById[inspectionId]) {
      setActiveAnalysis(analysisById[inspectionId])
      return
    }
    setAnalyzingId(inspectionId)
    setAnalysisError(null)
    try {
      const result = await qualityApi.analyzeQualityRisk(inspectionId)
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

  async function openRecordModal() {
    setIsRecordOpen(true)
    setRecordError(null)
    setPreviewImage(null)
    try {
      const delList = await qualityApi.listDeliveries()
      const normalized = Array.isArray(delList) ? delList : (delList.deliveries ?? [])
      setDeliveries(normalized)
      if (normalized.length > 0 && !form.deliveryId) {
        const item = normalized[0].items?.[0]
        setForm((prev) => ({ ...prev, deliveryId: String(normalized[0].id), materialId: item?.materialId ?? '', materialName: item?.material?.name ?? item?.materialName ?? '', inspectedQuantity: item?.receivedQuantity ?? 0 }))
      }
    } catch {
      setDeliveries([])
    }
  }

  async function handleRecordSubmit(e) {
    e.preventDefault()
    setRecordError(null)
    const inspected = Number(form.inspectedQuantity)
    const rejected = Number(form.rejectedQuantity)

    if (!form.deliveryId) {
      setRecordError('Please select a received delivery to inspect.')
      return
    }
    if (!Number.isFinite(inspected) || inspected <= 0) {
      setRecordError('Inspected quantity must be greater than zero.')
      return
    }
    if (!Number.isFinite(rejected) || rejected < 0 || rejected > inspected) {
      setRecordError('Rejected quantity cannot be negative or exceed inspected quantity.')
      return
    }
    if (rejected > 0 && !form.rejectionReason.trim()) {
      setRecordError('A rejection reason is required when rejecting material.')
      return
    }

    if (['quantityCheck', 'visualConditionCheck', 'moistureCheck', 'packagingCheck', 'defectsCheck'].some(key => !form[key]) && !form.notes.trim()) {
      setRecordError('Notes are required when any checklist item fails.')
      return
    }
    const delivery = deliveries.find(d => String(d.id) === String(form.deliveryId))
    const lines = (delivery?.items ?? []).filter(item => Number(item.materialId) === Number(form.materialId))
    if (!lines.length) {
      setRecordError('Material must belong to the selected delivery.')
      return
    }
    if (inspected > lines.reduce((sum, item) => sum + Number(item.receivedQuantity || 0), 0)) {
      setRecordError('Inspected quantity cannot exceed received quantity.')
      return
    }

    setSubmittingInspection(true)
    try {
      const payload = {
        deliveryId: Number(form.deliveryId),
        inspectionCriteria: form.inspectionCriteria,
        observedResult: form.observedResult,
        notes: form.notes,
        quantityCheck: Boolean(form.quantityCheck),
        visualConditionCheck: Boolean(form.visualConditionCheck),
        moistureCheck: Boolean(form.moistureCheck),
        packagingCheck: Boolean(form.packagingCheck),
        defectsCheck: Boolean(form.defectsCheck),
        items: [
          {
            materialId: Number(form.materialId),
            inspectedQuantity: inspected,
            acceptedQuantity: Number((inspected - rejected).toFixed(2)),
            rejectedQuantity: rejected,
            rejectionReason: rejected > 0 ? form.rejectionReason.trim() : '',
          },
        ],
        evidence: form.evidenceFileUrl.trim() ? [
          {
            fileName: form.evidenceFileName.trim() || 'inspection-evidence',
            fileUrl: form.evidenceFileUrl.trim(),
            contentType: form.evidenceContentType,
            fileSizeBytes: form.evidenceFileSizeBytes,
          },
        ] : [],
      }
      const created = await qualityApi.completeInspection(payload)
      setRecordNotice(`Inspection INS-${created.id} recorded successfully!${rejected > 0 ? ' Non-conformance report (NCR) raised automatically.' : ''}`)
      setIsRecordOpen(false)
      setPreviewImage(null)
      setForm(INITIAL_INSPECTION_FORM)
      await load()
    } catch (err) {
      setRecordError(err.message)
    } finally {
      setSubmittingInspection(false)
    }
  }

  const items = (i) => i.items ?? []
  const totalInspected = inspections.reduce((sum, i) => sum + items(i).reduce((s, it) => s + (it.inspectedQuantity ?? 0), 0), 0)
  const totalRejected = inspections.reduce((sum, i) => sum + items(i).reduce((s, it) => s + (it.rejectedQuantity ?? 0), 0), 0)
  const notFullyAccepted = inspections.filter((i) => i.overallDecision && i.overallDecision !== 'Accepted').length

  const summaryCards = [
    ['Inspections', inspections.length, 'Completed quality records', '#2563eb'],
    ['Not fully accepted', notFullyAccepted, 'Partial or rejected result', '#b45309'],
    ['Units inspected', totalInspected, 'Across all recorded lines', '#0f766e'],
    ['Units rejected', totalRejected, 'Rejected inspection lines automatically raise NCRs', '#b91c1c'],
  ]

  const inspectedNum = Number(form.inspectedQuantity || 0)
  const rejectedNum = Number(form.rejectedQuantity || 0)
  const acceptedNum = Math.max(0, inspectedNum - rejectedNum)
  const previewResult = rejectedNum === 0 ? 'Accepted' : rejectedNum === inspectedNum ? 'Rejected' : 'PartiallyAccepted'

  return (
    <div className="stack">
      <PageHeader
        title="Quality Inspections"
        description="Material accepted, partially accepted or rejected on site. Rejected lines automatically raise a non-conformance."
        actions={canInspect ? <Button onClick={openRecordModal}>+ Record Inspection</Button> : <StatusBadge status="neutral">Read only</StatusBadge>}
      />

      {/* The recording outcome pops up so it cannot be scrolled past. */}
      <SuccessDialog
        open={Boolean(recordNotice)}
        title="Inspection recorded"
        message={recordNotice ?? ''}
        confirmLabel="OK"
        onClose={() => setRecordNotice(null)}
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
            actionLabel={canInspect ? '+ Record Inspection' : undefined}
            onAction={canInspect ? openRecordModal : undefined}
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
                  <th>Inspector comments</th>
                  <th>Evidence</th>
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
                      <td style={{ minWidth: 160, maxWidth: 280, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{inspection.notes || '—'}</td>
                      <td>
                        {(inspection.evidence || []).length === 0 ? 'No evidence' : inspection.evidence.map((file, index) => {
                          const url = file.fileUrl || ''
                          const safe = /^https?:\/\//i.test(url) || /^data:image\/[^;]+;base64,/i.test(url)
                          const image = /^data:image\//i.test(url) || /^image\//i.test(file.contentType || '') || /\.(png|jpe?g|webp|gif)(\?|$)/i.test(url)
                          return <div key={file.id || index} style={{ marginBottom: 8 }}>
                            {safe && image && <img src={url} alt={file.fileName || 'Inspection evidence'} loading="lazy" style={{ display: 'block', width: 88, height: 72, objectFit: 'cover', borderRadius: 6 }} />}
                            {safe ? <a href={url} target="_blank" rel="noopener noreferrer" download={url.startsWith('data:') ? (file.fileName || 'inspection-evidence') : undefined}>{file.fileName || 'View evidence'}</a> : <span>{file.fileName || 'Evidence file'}</span>}
                          </div>
                        })}
                      </td>
                      <td>
                        <Button
                          variant="secondary"
                          onClick={() => runRiskAnalysis(inspection.id)}
                          disabled={analyzingId === inspection.id}
                        >
                          {analyzingId === inspection.id ? 'Analysing…' : 'Run AI Analysis'}
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

      {/* ── AI Risk Assessment Drawer ── */}
      <Drawer
        open={activeAnalysis != null}
        title={`AI Quality Risk — INS-${activeAnalysis?.inspectionId}`}
        subtitle="QualityRiskAnalysisAgent advisory risk assessment"
        onClose={() => setActiveAnalysis(null)}
      >
        {activeAnalysis && <QualityRiskPanel analysis={activeAnalysis} />}
      </Drawer>

      {/* ── Record Inspection Drawer Form ── */}
      <Drawer
        open={isRecordOpen}
        title="Record Quality Inspection"
        subtitle="Complete the 5-point quality checklist. Rejections automatically raise an NCR."
        onClose={() => setIsRecordOpen(false)}
      >
        <form className="stack" onSubmit={handleRecordSubmit}>
          {recordError && <div className="field__error" role="alert">⚠ {recordError}</div>}

          <Card title="1. Delivery & Material">
            <div className="form-grid">
              <SelectInput
                label="Target Delivery"
                value={form.deliveryId}
                onChange={(e) => {
                  const item = deliveries.find(d => String(d.id) === e.target.value)?.items?.[0]
                  setForm(p => ({ ...p, deliveryId: e.target.value, materialId: item?.materialId ?? '', materialName: item?.material?.name ?? item?.materialName ?? '', inspectedQuantity: item?.receivedQuantity ?? 0 }))
                }}
                options={[
                  { value: '', label: 'Select a received delivery…' },
                  ...deliveries.map((d) => ({
                    value: String(d.id),
                    label: `DEL-${d.id} · ${d.deliveryReference || 'Delivery'} (PO-${d.purchaseOrderId})`,
                  })),
                ]}
              />

              <TextInput
                label="Material Description"
                value={form.materialName}
                onChange={(e) => setForm((p) => ({ ...p, materialName: e.target.value }))}
                disabled
              />
            </div>
          </Card>

          <Card title="2. 5-Point Quality Checklist" subtitle="Every criterion must be evaluated before completing the inspection.">
            <div className="stack" style={{ gap: '0.75rem' }}>
              {[
                ['quantityCheck', '1. Quantity verified against delivery invoice & PO'],
                ['visualConditionCheck', '2. Visual condition acceptable (no physical damage / bag tears)'],
                ['moistureCheck', '3. Moisture check passed (dry, lump-free, unhardened)'],
                ['packagingCheck', '4. Packaging intact and standard manufacturer seal verified'],
                ['defectsCheck', '5. No visible structural or transit defects detected'],
              ].map(([key, labelText]) => (
                <label
                  key={key}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.6rem',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    border: '1px solid var(--color-border)',
                    background: form[key] ? 'var(--color-success-50, #f0fdf4)' : 'var(--color-danger-50, #fef2f2)',
                    cursor: 'pointer',
                    fontSize: 'var(--font-sm)',
                    fontWeight: 600,
                  }}
                >
                  <input
                    type="checkbox"
                    checked={Boolean(form[key])}
                    onChange={(e) => setForm((p) => ({ ...p, [key]: e.target.checked }))}
                  />
                  <span>{labelText}</span>
                </label>
              ))}
            </div>
          </Card>

          <Card title="3. Quantities & Outcome">
            <div className="form-grid">
              <TextInput
                label="Inspected Quantity"
                type="number"
                min="0"
                step="0.01"
                value={String(form.inspectedQuantity)}
                onChange={(e) => setForm((p) => ({ ...p, inspectedQuantity: Number(e.target.value) }))}
              />
              <TextInput
                label="Rejected Quantity"
                type="number"
                min="0"
                step="0.01"
                max={String(form.inspectedQuantity)}
                value={String(form.rejectedQuantity)}
                onChange={(e) => setForm((p) => ({ ...p, rejectedQuantity: Number(e.target.value) }))}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '0.75rem', padding: '0.75rem', background: 'var(--color-surface-muted)', borderRadius: '6px' }}>
              <span>Accepted Quantity: <strong>{acceptedNum} units</strong></span>
              <span>Result: <StatusBadge status={previewResult === 'Accepted' ? 'success' : previewResult === 'PartiallyAccepted' ? 'warning' : 'danger'}>{previewResult}</StatusBadge></span>
            </div>

            {rejectedNum > 0 && (
              <div style={{ marginTop: '0.75rem' }}>
                <TextInput
                  label="Rejection Reason (Required for NCR)"
                  value={form.rejectionReason}
                  onChange={(e) => setForm((p) => ({ ...p, rejectionReason: e.target.value }))}
                  placeholder="e.g. 40 bags wet and hardened due to rain during transit"
                  required
                />
              </div>
            )}
          </Card>

          <Card title="4. Evidence & Inspector Comments" subtitle="Attach site photos, test certificates, or capture directly with device camera.">
            <input
              type="file"
              ref={fileInputRef}
              accept="image/*"
              style={{ display: 'none' }}
              onChange={(e) => { if (e.target.files?.[0]) handleImageFile(e.target.files[0]) }}
            />
            <input
              type="file"
              ref={cameraInputRef}
              accept="image/*"
              capture="environment"
              style={{ display: 'none' }}
              onChange={(e) => { if (e.target.files?.[0]) handleImageFile(e.target.files[0]) }}
            />

            <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', marginBottom: '1rem' }}>
              <Button
                variant="secondary"
                type="button"
                onClick={() => cameraInputRef.current?.click()}
                style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
              >
                📷 Open Camera / Capture
              </Button>
              <Button
                variant="secondary"
                type="button"
                onClick={() => fileInputRef.current?.click()}
                style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
              >
                📁 Upload Evidence Photo
              </Button>
            </div>

            {previewImage && (
              <div
                style={{
                  marginBottom: '1rem',
                  padding: '0.75rem',
                  borderRadius: '8px',
                  border: '1px solid var(--color-border)',
                  background: 'var(--color-surface-muted)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '1rem',
                  justifyContent: 'space-between',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                  <img
                    src={previewImage}
                    alt="Evidence Preview"
                    style={{ width: '64px', height: '64px', objectFit: 'cover', borderRadius: '6px', border: '1px solid #ccc' }}
                  />
                  <div>
                    <strong style={{ fontSize: '0.9rem', display: 'block' }}>{form.evidenceFileName || 'Captured Photo'}</strong>
                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Photo attached and ready for inspection record</span>
                  </div>
                </div>
                <Button
                  variant="secondary"
                  type="button"
                  onClick={() => {
                    setPreviewImage(null)
                    setForm((p) => ({ ...p, evidenceFileName: '', evidenceFileUrl: '' }))
                  }}
                  style={{ fontSize: '0.8rem', padding: '0.3rem 0.6rem' }}
                >
                  ✕ Remove
                </Button>
              </div>
            )}

            <div className="form-grid">
              <TextInput
                label="Evidence File Name (Optional)"
                value={form.evidenceFileName}
                onChange={(e) => setForm((p) => ({ ...p, evidenceFileName: e.target.value }))}
                placeholder="e.g. moisture-test-cert.jpg"
              />
              <TextInput
                label="Evidence File URL (Optional)"
                value={form.evidenceFileUrl}
                onChange={(e) => setForm((p) => ({ ...p, evidenceFileUrl: e.target.value }))}
                placeholder="e.g. https://storage.buildwise.demo/qc/img-01.jpg"
              />
            </div>

            <div style={{ marginTop: '0.75rem' }}>
              <TextInput
                label="Inspector Comments / Notes"
                multiline
                value={form.notes}
                onChange={(e) => setForm((p) => ({ ...p, notes: e.target.value }))}
                placeholder="Site condition notes or re-inspection remarks..."
              />
            </div>
          </Card>

          <div className="form-actions">
            <Button variant="secondary" type="button" onClick={() => setIsRecordOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={submittingInspection || !form.deliveryId}>
              {submittingInspection ? 'Submitting Inspection…' : 'Submit Inspection'}
            </Button>
          </div>
        </form>
      </Drawer>
    </div>
  )
}

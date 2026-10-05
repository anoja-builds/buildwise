import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import QualityInspectionsPage from './QualityInspectionsPage'
import NonConformancesPage from './NonConformancesPage'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'
import { procurementApi } from '../Features/procurement/services/procurementApi'

vi.mock('../services/qualityApi', () => ({
  qualityApi: {
    listNonConformances: vi.fn(),
    listInspections: vi.fn(),
    getInspection: vi.fn(),
    analyzeQualityRisk: vi.fn(),
    transitionNonConformance: vi.fn(),
    listMaterialRequests: vi.fn(),
    listDeliveries: vi.fn(),
    completeInspection: vi.fn(),
  },
}))

// The lifecycle flow reads counts from the procurement side too.
vi.mock('../Features/procurement/services/procurementApi', () => ({
  procurementApi: { listRfqs: vi.fn(), listPurchaseOrders: vi.fn() },
}))

vi.mock('../auth/AuthContext', () => ({ useAuth: vi.fn() }))

const inspections = [
  {
    id: 34,
    deliveryId: 33,
    overallDecision: 'PartiallyAccepted',
    inspectionCriteria: 'Visual',
    observedResult: '5 damaged',
    evidence: [],
    inspectedAt: '2026-09-28T07:54:00Z',
    // A recorded checklist: quantity and defects passed, the physical
    // characteristics failed (consistent with 5 damaged bags).
    quantityCheck: true,
    visualConditionCheck: false,
    moistureCheck: false,
    packagingCheck: false,
    defectsCheck: true,
  },
  {
    id: 33,
    deliveryId: 32,
    overallDecision: 'Accepted',
    inspectionCriteria: 'Visual',
    observedResult: 'All good',
    evidence: [],
    inspectedAt: '2026-09-28T07:41:00Z',
    // No checklist fields: this is a legacy inspection recorded before the
    // structured checklist existed, and must render as "not recorded".
  },
]

const mediumRisk = {
  inspectionId: 34,
  deliveryId: 33,
  agent: 'QualityRiskAnalysisAgent',
  tool: 'analyze_quality_risk',
  executionSource: 'PythonQualityAgent',
  riskLevel: 'Medium',
  requiresNcr: true,
  suggestedCorrectiveAction: 'Issue formal NCR to supplier for defective materials.',
  riskFlags: ['MINOR_QUALITY_DEFECT'],
  totalInspected: 240,
  totalRejected: 5,
  rejectionRatePct: 2.08,
  inspectionStatus: 'Completed',
  overallDecision: 'PartiallyAccepted',
}

// Shape mirrors the real /quality-inspections/non-conformances response, so the
// detail view is exercised against the full delivery → inspection → item chain.
const ncr = {
  id: 35,
  ncrNumber: 'NCR-781611',
  inspectionItemId: 33,
  inspectionItem: {
    id: 33,
    inspectionId: 35,
    material: { id: 1, name: 'Cement (50kg bag)', unit: 'bag' },
    inspectedQuantity: 240,
    acceptedQuantity: 235,
    rejectedQuantity: 5,
    rejectionReason: 'Water damage during transport.',
    inspection: {
      id: 35,
      deliveryId: 34,
      overallDecision: 'PartiallyAccepted',
      delivery: { id: 34, deliveryReference: 'E2E-20260928083239685', status: 'DiscrepancyReported' },
    },
  },
  deliveryId: 34,
  materialId: 1,
  supplierId: 1,
  quantityAffected: 5,
  severity: 'Medium',
  status: 'CorrectiveActionRequired',
  issueDescription: 'Water damage during transport.',
  correctiveActionPlan: 'Issue formal NCR to supplier for defective materials. Request credit note or replacement for rejected quantity.',
  resolution: null,
  reviewNotes: null,
  reviewedByUserId: null,
  reviewedAt: null,
  resolvedAt: null,
  closedAt: null,
  createdAt: '2026-09-28T08:32:42Z',
}

describe('QualityInspectionsPage', () => {
  it('shows saved inspector comments and evidence photos in inspection history', async () => {
    qualityApi.listInspections.mockResolvedValueOnce([{
      ...inspections[0], notes: 'Packaging inspected at the unloading bay.',
      evidence: [{ id: 1, fileName: 'site-photo.jpg', fileUrl: 'https://example.test/site-photo.jpg', contentType: 'image/jpeg' }],
    }])
    render(<QualityInspectionsPage />)
    expect(await screen.findByText('Packaging inspected at the unloading bay.')).toBeInTheDocument()
    expect(screen.getByAltText('site-photo.jpg')).toHaveAttribute('src', 'https://example.test/site-photo.jpg')
    expect(screen.getByRole('link', { name: 'site-photo.jpg' })).toHaveAttribute('href', 'https://example.test/site-photo.jpg')
  })
  beforeEach(() => {
    vi.clearAllMocks()
    qualityApi.listNonConformances.mockResolvedValue([])
    qualityApi.listInspections.mockResolvedValue(inspections)
    // The lifecycle flow reads counts from several endpoints; settle them so an
    // unstubbed call cannot reject and leave a stage showing "—".
    qualityApi.listMaterialRequests.mockResolvedValue([])
    qualityApi.listDeliveries.mockResolvedValue([])
    procurementApi.listRfqs.mockResolvedValue([])
    procurementApi.listPurchaseOrders.mockResolvedValue({ total: 0 })
    // `roles` is read by the lifecycle flow to decide which stages are visible.
    useAuth.mockReturnValue({ hasRole: () => true, roles: ['QualityInspector'] })
  })

  it('lists inspections and offers an AI analysis action for each', async () => {
    render(<QualityInspectionsPage />)

    expect(await screen.findByText('INS-34')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Run AI Analysis' })).toHaveLength(2)
    // Nothing is claimed before the agent actually runs.
    expect(screen.queryByTestId('quality-risk-panel')).not.toBeInTheDocument()
  })

  it('surfaces the real agent result with its risk level and NCR recommendation', async () => {
    const user = userEvent.setup()
    qualityApi.analyzeQualityRisk.mockResolvedValue(mediumRisk)
    render(<QualityInspectionsPage />)
    await screen.findByText('INS-34')

    await user.click(screen.getAllByRole('button', { name: 'Run AI Analysis' })[0])

    const panel = await screen.findByTestId('quality-risk-panel')
    expect(within(panel).getByText('QualityRiskAnalysisAgent')).toBeInTheDocument()
    expect(within(panel).getByText('analyze_quality_risk')).toBeInTheDocument()
    // Proves the real Python agent answered, not the deterministic fallback.
    expect(within(panel).getByText('PythonQualityAgent')).toBeInTheDocument()
    expect(within(panel).getByText('Medium')).toBeInTheDocument()
    expect(within(panel).getByText('Required by this assessment')).toBeInTheDocument()
    expect(within(panel).getByText('MINOR_QUALITY_DEFECT')).toBeInTheDocument()
    expect(qualityApi.analyzeQualityRisk).toHaveBeenCalledWith(34)
  })

  it('reports a low-risk inspection as not requiring an NCR', async () => {
    const user = userEvent.setup()
    qualityApi.analyzeQualityRisk.mockResolvedValue({
      ...mediumRisk,
      inspectionId: 33,
      riskLevel: 'Low',
      requiresNcr: false,
      riskFlags: [],
      totalRejected: 0,
      rejectionRatePct: 0,
      suggestedCorrectiveAction: 'No corrective action required. Material meets quality standards.',
    })
    render(<QualityInspectionsPage />)
    await screen.findByText('INS-33')

    await user.click(screen.getAllByRole('button', { name: 'Run AI Analysis' })[1])

    const panel = await screen.findByTestId('quality-risk-panel')
    expect(within(panel).getByText('Low')).toBeInTheDocument()
    expect(within(panel).getByText('Not required')).toBeInTheDocument()
    expect(within(panel).getByText('None')).toBeInTheDocument()
  })

  it('keeps the inspection list readable when the agent call fails', async () => {
    const user = userEvent.setup()
    qualityApi.analyzeQualityRisk.mockRejectedValue(new Error('Quality agent unavailable'))
    render(<QualityInspectionsPage />)
    await screen.findByText('INS-34')

    await user.click(screen.getAllByRole('button', { name: 'Run AI Analysis' })[0])

    expect(await screen.findByText('Quality agent unavailable')).toBeInTheDocument()
    // The authoritative inspection records are still shown.
    expect(screen.getByText('INS-34')).toBeInTheDocument()
    expect(screen.getByText('INS-33')).toBeInTheDocument()
  })

  it('does not file one inspection analysis under another row', async () => {
    const user = userEvent.setup()
    qualityApi.analyzeQualityRisk.mockResolvedValue(mediumRisk)
    render(<QualityInspectionsPage />)
    await screen.findByText('INS-34')

    await user.click(screen.getAllByRole('button', { name: 'Run AI Analysis' })[0])
    await screen.findByTestId('quality-risk-panel')
    // Exactly one panel, for the row that was analysed.
    expect(screen.getAllByTestId('quality-risk-panel')).toHaveLength(1)
    // Both rows keep their own action, so the analysed row was not replaced.
    expect(screen.getAllByRole('button', { name: 'Run AI Analysis' })).toHaveLength(2)
  })

  it('states that the agent is advisory while the inspection record is authoritative', async () => {
    const user = userEvent.setup()
    qualityApi.analyzeQualityRisk.mockResolvedValue(mediumRisk)
    render(<QualityInspectionsPage />)
    await screen.findByText('INS-34')
    await user.click(screen.getAllByRole('button', { name: 'Run AI Analysis' })[0])

    const panel = await screen.findByTestId('quality-risk-panel')
    // The viva point: the agent recommends, the backend/inspector decide.
    expect(within(panel).getByText(/Advisory only/)).toBeInTheDocument()
    expect(within(panel).getByText('Completed · Partially Accepted')).toBeInTheDocument()
  })

  it('shows the error state when the page cannot load', async () => {
    qualityApi.listInspections.mockRejectedValueOnce(new Error('API unavailable'))
    render(<QualityInspectionsPage />)

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
  })

  it('shows an empty state when no inspections exist', async () => {
    qualityApi.listInspections.mockResolvedValue([])
    render(<QualityInspectionsPage />)

    expect(await screen.findByText('No inspections recorded')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Run AI Analysis' })).not.toBeInTheDocument())
  })

  it('renders the five-point checklist as structured pass/fail rows', async () => {
    render(<QualityInspectionsPage />)

    const row = (await screen.findByText('INS-34')).closest('tr')

    // true -> a tick, false -> a cross. The criteria are listed individually
    // rather than collapsed into one free-text sentence.
    expect(within(row).getByText('✓ Quantity')).toBeInTheDocument()
    expect(within(row).getByText('✗ Visual condition')).toBeInTheDocument()
    expect(within(row).getByText('✗ Moisture')).toBeInTheDocument()
    expect(within(row).getByText('✗ Packaging')).toBeInTheDocument()
    expect(within(row).getByText('✓ Defects')).toBeInTheDocument()
  })

  it('marks a legacy inspection as not recorded instead of passing it', async () => {
    render(<QualityInspectionsPage />)

    const row = (await screen.findByText('INS-33')).closest('tr')

    // An inspection recorded before the structured checklist existed must never
    // render as "✓" — that would fabricate a quality result.
    expect(within(row).getByText('Not recorded')).toBeInTheDocument()
    expect(within(row).queryByText('✓ Quantity')).not.toBeInTheDocument()
  })

  it('opens the full non-conformance record from the NCR number', async () => {
    const user = userEvent.setup()
    qualityApi.listNonConformances.mockResolvedValue([ncr])
    // The NCR record moved to its own screen when the page was split.
    render(<NonConformancesPage />)

    // The table alone only shows a summary; the evidence chain is behind a click.
    await user.click(await screen.findByRole('button', { name: 'NCR-781611' }))

    const dialog = await screen.findByRole('dialog', { name: 'NCR-781611' })
    // Material, origin and the three quantities prove the record is complete.
    expect(within(dialog).getByText('Cement (50kg bag)')).toBeInTheDocument()
    expect(within(dialog).getByText('DEL-34')).toBeInTheDocument()
    expect(within(dialog).getByText('INS-35')).toBeInTheDocument()
    expect(within(dialog).getByText('240 units')).toBeInTheDocument()
    expect(within(dialog).getByText('5 units')).toBeInTheDocument()
    // Review trail is explicit about what has not happened yet.
    expect(within(dialog).getByText('Not yet resolved')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'NCR-781611' })).not.toBeInTheDocument())
  })

  it('shows the inspection summary cards on the inspections screen', async () => {
    render(<QualityInspectionsPage />)
    await screen.findByText('INS-34')

    expect(screen.getByText('Inspections')).toBeInTheDocument()
    expect(screen.getByText('Units inspected')).toBeInTheDocument()
    expect(screen.getByText('Units rejected')).toBeInTheDocument()
  })
})

describe('NonConformancesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    qualityApi.listNonConformances.mockResolvedValue([ncr])
    qualityApi.listInspections.mockResolvedValue([])
    qualityApi.listMaterialRequests.mockResolvedValue([])
    qualityApi.listDeliveries.mockResolvedValue([])
    procurementApi.listRfqs.mockResolvedValue([])
    procurementApi.listPurchaseOrders.mockResolvedValue({ total: 0 })
    useAuth.mockReturnValue({ hasRole: () => true, roles: ['ProcurementManager'] })
  })

  it('lists non-conformances and displays summary cards', async () => {
    render(<NonConformancesPage />)

    expect(await screen.findByText('NCR-781611')).toBeInTheDocument()
    expect(screen.getByText('Total NCRs')).toBeInTheDocument()
  })

  it('does not show the inspections list on the non-conformance screen', async () => {
    render(<NonConformancesPage />)
    await screen.findByText('NCR-781611')

    // The two screens are genuinely separate now.
    expect(screen.queryByText('INS-34')).not.toBeInTheDocument()
  })

  it('refuses a resolution-bearing transition with no resolution text', async () => {
    const user = userEvent.setup()
    render(<NonConformancesPage />)
    await screen.findByText('NCR-781611')

    // Pre-select the Resolved status, leave the resolution blank, save.
    await user.selectOptions(
      screen.getByLabelText('Transition status for NCR-781611'),
      'Resolved',
    )
    await user.click(screen.getByRole('button', { name: 'Save transition' }))

    // Guarded client-side, so no pointless round trip is made.
    expect(await screen.findByText(/resolution is required/i)).toBeInTheDocument()
    expect(qualityApi.transitionNonConformance).not.toHaveBeenCalled()
  })

  it('submits the transition with the resolution when one is provided', async () => {
    const user = userEvent.setup()
    qualityApi.transitionNonConformance.mockResolvedValue({ id: ncr.id, status: 'Resolved' })
    render(<NonConformancesPage />)
    await screen.findByText('NCR-781611')

    await user.selectOptions(
      screen.getByLabelText('Transition status for NCR-781611'),
      'Resolved',
    )
    await user.type(screen.getByLabelText('Resolution'), 'Supplier replaced 20 bags.')
    await user.click(screen.getByRole('button', { name: 'Save transition' }))

    await waitFor(() => expect(qualityApi.transitionNonConformance).toHaveBeenCalledWith(ncr.id, expect.objectContaining({
      status: 'Resolved',
      resolution: 'Supplier replaced 20 bags.',
    })))
  })

  it('hides the review controls from a role that cannot approve', async () => {
    useAuth.mockReturnValue({ hasRole: () => false, roles: ['QualityInspector'] })
    render(<NonConformancesPage />)
    await screen.findByText('NCR-781611')

    // A Quality Inspector records inspections; they do not close NCRs.
    expect(screen.queryByRole('button', { name: 'Save transition' })).not.toBeInTheDocument()
    expect(screen.getByText('Awaiting Procurement Manager review')).toBeInTheDocument()
  })
})

describe('Inspection form validation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useAuth.mockReturnValue({ hasRole: () => true, roles: ['QualityInspector'] })
    qualityApi.listInspections.mockResolvedValue([])
    qualityApi.listDeliveries.mockResolvedValue([{ id: 34, items: [{ materialId: 7, materialName: 'Rebar', receivedQuantity: 20 }] }])
  })

  async function openForm() {
    const user = userEvent.setup()
    render(<QualityInspectionsPage />)
    await user.click((await screen.findAllByRole('button', { name: /Record Inspection/i }))[0])
    await waitFor(() => expect(screen.getByLabelText('Inspected Quantity')).toHaveValue(20))
    return user
  }

  it('requires Notes when a checklist item fails', async () => {
    const user = await openForm()
    await user.click(screen.getByRole('checkbox', { name: /Quantity verified/i }))
    await user.click(screen.getByRole('button', { name: 'Submit Inspection' }))
    expect(await screen.findByText(/Notes are required when any checklist item fails/)).toBeInTheDocument()
    expect(qualityApi.completeInspection).not.toHaveBeenCalled()
  })

  it('rejects quantities exceeding the selected material received quantity', async () => {
    const user = await openForm()
    await user.clear(screen.getByLabelText('Inspected Quantity'))
    await user.type(screen.getByLabelText('Inspected Quantity'), '21')
    await user.click(screen.getByRole('button', { name: 'Submit Inspection' }))
    expect(await screen.findByText(/Inspected quantity cannot exceed received quantity/)).toBeInTheDocument()
    expect(qualityApi.completeInspection).not.toHaveBeenCalled()
  })
})

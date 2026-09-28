/**
 * QualityInspectionWorkflow.test.jsx
 *
 * Focused React tests covering:
 *  1. Pending deliveries load
 *  2. Start inspection success
 *  3. Complete inspection payload
 *  4. Validation / error rendering
 *  5. Completed inspection refresh
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import QualityApp from './QualityApp'
import { useAuth } from '../../auth/AuthContext'
import { qualityApi } from './services/qualityApi'

// ── Mocks ──────────────────────────────────────────────────────────────────

vi.mock('../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('./services/qualityApi', () => ({
  qualityApi: Object.fromEntries(
    [
      'listInspections', 'getInspection', 'pendingDeliveries',
      'startInspection', 'completeInspection',
      'listNcrs', 'getNcr', 'createNcr',
      'updateCorrectiveAction', 'resolveNcr', 'closeNcr',
      'analyseInspection', 'getWorkflow'
    ].map((key) => [key, vi.fn()])
  )
}))

// Stub QualityRiskPanel to keep tests focused on inspection workflow
vi.mock('./QualityRiskPanel', () => ({ default: () => null }))

// ── Fixtures ───────────────────────────────────────────────────────────────

const pendingDelivery = {
  deliveryId: 10,
  deliveryReference: 'DEL-TEST-001',
  status: 'Received',
  items: [
    { deliveryItemId: 101, purchaseOrderItemId: 201, receivedQuantity: 50, damagedQuantity: 5 },
    { deliveryItemId: 102, purchaseOrderItemId: 202, receivedQuantity: 30, damagedQuantity: 0 }
  ]
}

const startedInspection = {
  id: 7,
  deliveryId: 10,
  deliveryReference: 'DEL-TEST-001',
  inspectorUserId: 3,
  inspectorName: 'Inspector Priya',
  inspectionDate: '2026-09-28T10:00:00Z',
  status: 'UnderInspection',
  overallDecision: null,
  notes: null,
  items: [],
  deliveryItems: [
    { deliveryItemId: 101, purchaseOrderItemId: 201, receivedQuantity: 50, damagedQuantity: 5 },
    { deliveryItemId: 102, purchaseOrderItemId: 202, receivedQuantity: 30, damagedQuantity: 0 }
  ]
}

const completedInspection = {
  ...startedInspection,
  status: 'Completed',
  overallDecision: 'PartiallyAccepted',
  items: [
    { id: 20, deliveryItemId: 101, condition: 'Wet bags', acceptedQuantity: 45, rejectedQuantity: 5, remarks: 'Moisture damage' },
    { id: 21, deliveryItemId: 102, condition: 'Good', acceptedQuantity: 30, rejectedQuantity: 0, remarks: null }
  ]
}

const historyRow = {
  id: 7,
  deliveryId: 10,
  deliveryReference: 'DEL-TEST-001',
  inspectorUserId: 3,
  inspectorName: 'Inspector Priya',
  inspectionDate: '2026-09-28T10:00:00Z',
  status: 'Completed',
  overallDecision: 'PartiallyAccepted'
}

// ── Setup ──────────────────────────────────────────────────────────────────

beforeEach(() => {
  vi.resetAllMocks()
  useAuth.mockReturnValue({ roles: ['QualityInspector'] })
  qualityApi.listInspections.mockResolvedValue([])
  qualityApi.pendingDeliveries.mockResolvedValue([])
  qualityApi.listNcrs.mockResolvedValue([])
})

const renderQuality = () => render(<QualityApp section="Quality Inspections" />)

// ── 1. Pending deliveries load ─────────────────────────────────────────────

describe('pending deliveries section', () => {
  it('shows a pending delivery row when one is returned', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    renderQuality()
    expect(await screen.findByText('DEL-TEST-001')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start Inspection' })).toBeInTheDocument()
  })

  it('shows received and damaged totals', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    renderQuality()
    await screen.findByText('DEL-TEST-001')
    // total received: 50 + 30 = 80
    expect(screen.getByText('80')).toBeInTheDocument()
    // total damaged: 5 + 0 = 5
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('shows an empty state when no deliveries are pending', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([])
    renderQuality()
    expect(await screen.findByText('No pending inspections')).toBeInTheDocument()
  })

  it('shows an error state when the API call fails', async () => {
    qualityApi.pendingDeliveries.mockRejectedValue(new Error('Network timeout'))
    renderQuality()
    expect(await screen.findByText('Network timeout')).toBeInTheDocument()
  })
})

// ── 2. Start inspection success ────────────────────────────────────────────

describe('start inspection', () => {
  it('navigates to start-inspection form on "Start Inspection" click', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    renderQuality()
    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    expect(await screen.findByText(/Start Inspection — DEL-TEST-001/)).toBeInTheDocument()
  })

  it('calls startInspection with deliveryId and notes from JWT — no inspectorUserId in body', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    qualityApi.startInspection.mockResolvedValue(startedInspection)
    qualityApi.getInspection.mockResolvedValue(startedInspection)
    renderQuality()
    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Start Inspection — DEL-TEST-001/)

    const notesInput = screen.getByLabelText(/Initial notes/)
    fireEvent.change(notesInput, { target: { value: 'First check' } })
    fireEvent.click(screen.getByRole('button', { name: 'Start Inspection' }))

    await waitFor(() =>
      expect(qualityApi.startInspection).toHaveBeenCalledExactlyOnceWith({
        deliveryId: 10,
        notes: 'First check'
      })
    )
    // Must NOT include an inspectorUserId — backend derives from JWT
    const [body] = qualityApi.startInspection.mock.calls[0]
    expect(body).not.toHaveProperty('inspectorUserId')
  })

  it('proceeds to complete-inspection form after successful start', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    qualityApi.startInspection.mockResolvedValue(startedInspection)
    renderQuality()
    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Start Inspection — DEL-TEST-001/)
    fireEvent.click(screen.getByRole('button', { name: 'Start Inspection' }))
    expect(await screen.findByText(/Complete Inspection #7/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Overall decision/)).toBeInTheDocument()
  })

  it('surfaces a backend error without navigating away', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    qualityApi.startInspection.mockRejectedValue(new Error('Delivery already has an inspection.'))
    renderQuality()
    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Start Inspection — DEL-TEST-001/)
    fireEvent.click(screen.getByRole('button', { name: 'Start Inspection' }))
    expect(await screen.findByText('Delivery already has an inspection.')).toBeInTheDocument()
    expect(screen.queryByText(/Complete Inspection/)).not.toBeInTheDocument()
  })
})

// ── 3. Complete inspection payload ─────────────────────────────────────────

describe('complete inspection payload', () => {
  async function openCompleteForm() {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    qualityApi.startInspection.mockResolvedValue(startedInspection)
    renderQuality()
    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Start Inspection — DEL-TEST-001/)
    fireEvent.click(screen.getByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Complete Inspection #7/)
  }

  it('sends the correct payload including all delivery items', async () => {
    await openCompleteForm()
    qualityApi.completeInspection.mockResolvedValue(completedInspection)
    qualityApi.getInspection.mockResolvedValue(completedInspection)

    // Fill item 101
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '45' } })
    fireEvent.change(screen.getByLabelText(/Rejected quantity/, { selector: '[name="rejected-0"]' }), { target: { value: '5' } })
    fireEvent.change(screen.getByLabelText(/Condition/, { selector: '[name="condition-0"]' }), { target: { value: 'Wet bags' } })

    // Fill item 102
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-1"]' }), { target: { value: '30' } })
    fireEvent.change(screen.getByLabelText(/Rejected quantity/, { selector: '[name="rejected-1"]' }), { target: { value: '0' } })

    // Select PartiallyAccepted
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'PartiallyAccepted' } })
    fireEvent.change(screen.getByLabelText(/Inspection notes/), { target: { value: 'Mixed quality batch' } })

    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))

    await waitFor(() => expect(qualityApi.completeInspection).toHaveBeenCalledOnce())
    const [id, body] = qualityApi.completeInspection.mock.calls[0]
    expect(id).toBe(7)
    expect(body.overallDecision).toBe('PartiallyAccepted')
    expect(body.notes).toBe('Mixed quality batch')
    expect(body.items).toHaveLength(2)
    expect(body.items[0]).toMatchObject({ deliveryItemId: 101, acceptedQuantity: 45, rejectedQuantity: 5 })
    expect(body.items[1]).toMatchObject({ deliveryItemId: 102, acceptedQuantity: 30, rejectedQuantity: 0 })
  })
})

// ── 4. Validation and error rendering ─────────────────────────────────────

describe('validation and error rendering', () => {
  async function openCompleteForm() {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    qualityApi.startInspection.mockResolvedValue(startedInspection)
    renderQuality()
    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Start Inspection — DEL-TEST-001/)
    fireEvent.click(screen.getByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Complete Inspection #7/)
  }

  it('blocks submission if no overall decision is selected', async () => {
    await openCompleteForm()
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))
    expect(await screen.findByText('Select an overall decision.')).toBeInTheDocument()
    expect(qualityApi.completeInspection).not.toHaveBeenCalled()
  })

  it('blocks Accepted when there is a rejected quantity', async () => {
    await openCompleteForm()
    // Item 101: 45 accepted, 5 rejected
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '45' } })
    fireEvent.change(screen.getByLabelText(/Rejected quantity/, { selector: '[name="rejected-0"]' }), { target: { value: '5' } })
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'Accepted' } })
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))
    expect(await screen.findByText('Accepted requires zero rejected quantity.')).toBeInTheDocument()
    expect(qualityApi.completeInspection).not.toHaveBeenCalled()
  })

  it('blocks Rejected when there is an accepted quantity', async () => {
    await openCompleteForm()
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '10' } })
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'Rejected' } })
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))
    expect(await screen.findByText('Rejected requires zero accepted quantity.')).toBeInTheDocument()
  })

  it('blocks PartiallyAccepted when rejected quantity is zero', async () => {
    await openCompleteForm()
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '30' } })
    // no rejected
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'PartiallyAccepted' } })
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))
    expect(await screen.findByText('PartiallyAccepted requires both accepted and rejected quantities.')).toBeInTheDocument()
  })

  it('blocks submission when accepted + rejected exceeds received', async () => {
    await openCompleteForm()
    // Item 101 received = 50, attempt acc=40 + rej=20 = 60 > 50
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '40' } })
    fireEvent.change(screen.getByLabelText(/Rejected quantity/, { selector: '[name="rejected-0"]' }), { target: { value: '20' } })
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'PartiallyAccepted' } })
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))
    expect(await screen.findByText(/exceeds received/)).toBeInTheDocument()
    expect(qualityApi.completeInspection).not.toHaveBeenCalled()
  })

  it('surfaces backend error without navigating away', async () => {
    await openCompleteForm()
    qualityApi.completeInspection.mockRejectedValue(new Error('Missing DeliveryItemId values: 102.'))
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '45' } })
    fireEvent.change(screen.getByLabelText(/Rejected quantity/, { selector: '[name="rejected-0"]' }), { target: { value: '5' } })
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'PartiallyAccepted' } })
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))
    expect(await screen.findByText('Missing DeliveryItemId values: 102.')).toBeInTheDocument()
    expect(screen.getByText(/Complete Inspection #7/)).toBeInTheDocument()
  })
})

// ── 5. Completed inspection refresh ───────────────────────────────────────

describe('completed inspection refresh', () => {
  it('navigates to the inspection detail after successful completion', async () => {
    qualityApi.pendingDeliveries.mockResolvedValue([pendingDelivery])
    qualityApi.startInspection.mockResolvedValue(startedInspection)
    qualityApi.completeInspection.mockResolvedValue(completedInspection)
    qualityApi.getInspection.mockResolvedValue(completedInspection)
    renderQuality()

    fireEvent.click(await screen.findByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Start Inspection — DEL-TEST-001/)
    fireEvent.click(screen.getByRole('button', { name: 'Start Inspection' }))
    await screen.findByText(/Complete Inspection #7/)

    // Fill PartiallyAccepted
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-0"]' }), { target: { value: '45' } })
    fireEvent.change(screen.getByLabelText(/Rejected quantity/, { selector: '[name="rejected-0"]' }), { target: { value: '5' } })
    fireEvent.change(screen.getByLabelText(/Accepted quantity/, { selector: '[name="accepted-1"]' }), { target: { value: '30' } })
    fireEvent.change(screen.getByLabelText(/Overall decision/), { target: { value: 'PartiallyAccepted' } })
    fireEvent.click(screen.getByRole('button', { name: 'Complete Inspection' }))

    // After success, inspection detail should be shown
    expect(await screen.findByText('Inspection #7')).toBeInTheDocument()
    expect(await screen.findByText('PartiallyAccepted')).toBeInTheDocument()
    expect(qualityApi.getInspection).toHaveBeenCalledWith(7)
  })

  it('shows NCR creation button for rejected items in the completed inspection detail', async () => {
    qualityApi.listInspections.mockResolvedValue([historyRow])
    qualityApi.getInspection.mockResolvedValue(completedInspection)
    render(<QualityApp section="Quality Inspections" />)
    fireEvent.click(await screen.findByRole('button', { name: 'Inspection #7' }))
    expect(await screen.findByRole('button', { name: 'Create NCR for item #20' })).toBeInTheDocument()
    // Item #21 has 0 rejected — should NOT show NCR button
    expect(screen.queryByRole('button', { name: 'Create NCR for item #21' })).not.toBeInTheDocument()
  })
})

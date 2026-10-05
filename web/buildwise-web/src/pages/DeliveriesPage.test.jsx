import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import DeliveriesPage from './DeliveriesPage'
import { qualityApi } from '../services/qualityApi'
import { useAuth } from '../auth/AuthContext'

vi.mock('../services/qualityApi', () => ({
  qualityApi: {
    listDeliveries: vi.fn(),
    listConfirmedPurchaseOrders: vi.fn(),
    recordDelivery: vi.fn(),
    analyzeDeliveryDiscrepancy: vi.fn(),
  },
}))

vi.mock('../auth/AuthContext', () => ({ useAuth: vi.fn() }))

const deliveries = [
  // These mirror the REAL receiving DTO from DeliveriesController.Project:
  // supplierName / materialName / materialUnit are flat, server-resolved
  // strings. The fixtures previously used a nested `purchaseOrder.supplier.name`
  // / `material.name` shape the API never returns, so the browser showed
  // "Supplier record not expanded" and "Material #1" while every test passed.
  { id: 21, purchaseOrderId: 5, deliveryReference: 'INV-2001', status: 'Received', deliveredAt: '2026-09-24T08:00:00Z', supplierName: 'BuildWise Materials', items: [{ id: 1, materialId: 1, materialName: 'Cement', materialUnit: 'bag', orderedQuantity: 100, receivedQuantity: 100, damagedQuantity: 0 }] },
  { id: 22, purchaseOrderId: 6, deliveryReference: 'INV-2002', status: 'DiscrepancyReported', deliveredAt: '2026-09-23T08:00:00Z', supplierName: 'Island Supply', items: [{ id: 2, materialId: 1, materialName: 'Cement', materialUnit: 'bag', orderedQuantity: 100, receivedQuantity: 90, damagedQuantity: 5 }] },
]
// Confirmed orders come from PurchaseOrderProjection.ToReceivingDto, which also
// flattens the material to materialName + unit.
const orders = [{ id: 5, expectedDeliveryDate: '2026-10-01', items: [{ id: 51, materialId: 1, materialName: 'Cement', unit: 'bags', orderedQuantity: 100 }] }, { id: 7, expectedDeliveryDate: '2026-10-03', items: [] }]

function renderPage() {
  return render(<MemoryRouter initialEntries={['/deliveries']}><DeliveriesPage /></MemoryRouter>)
}

describe('DeliveriesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    qualityApi.listDeliveries.mockResolvedValue(deliveries)
    qualityApi.listConfirmedPurchaseOrders.mockResolvedValue(orders)
    useAuth.mockReturnValue({ hasRole: () => true, user: { fullName: 'Site Officer' } })
  })

  it('shows the summary, workflow and delivery table as the page content', async () => {
    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'Deliveries' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Delivery list' })).toBeInTheDocument()
    // Compact counters replace the old verbose dashboard. "Awaiting" appears in
    // both a counter and the workflow bar, so assert on the distinctive ones.
    expect(screen.getByText('Records')).toBeInTheDocument()
    expect(screen.getByText('Issues')).toBeInTheDocument()
    expect(screen.getByText('Today')).toBeInTheDocument()
    expect(screen.getAllByText('Awaiting').length).toBeGreaterThan(0)
    expect(screen.getByText('Workflow')).toBeInTheDocument()
  })

  it('never renders design-system documentation as page content', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 1, name: 'Deliveries' })

    expect(screen.queryByText('UI states')).not.toBeInTheDocument()
    expect(screen.queryByText('Status styles')).not.toBeInTheDocument()
    expect(screen.queryByText(/Shared feedback, loading, and status components/)).not.toBeInTheDocument()
  })

  it('never leaks internal component wording into user-facing copy', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 1, name: 'Deliveries' })

    expect(screen.queryByText(/Component 3/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Reusable detail view/)).not.toBeInTheDocument()
  })

  it('shows the error state instead of the page when loading fails', async () => {
    qualityApi.listDeliveries.mockRejectedValueOnce(new Error('API unavailable'))
    renderPage()

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Delivery list' })).not.toBeInTheDocument()
  })

  it('shows the empty state when there are no deliveries', async () => {
    qualityApi.listDeliveries.mockResolvedValue([])
    renderPage()

    expect(await screen.findByText('No matching deliveries')).toBeInTheDocument()
  })

  it('filters the delivery list', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    await user.type(screen.getByLabelText('Search'), 'INV-2002')

    const table = screen.getByRole('table')
    await waitFor(() => expect(within(table).queryByText('INV-2001')).not.toBeInTheDocument())
    expect(within(table).getByText('INV-2002')).toBeInTheDocument()
  })

  it('renders the supplier and material names returned by the API', async () => {
    // Regression: the page read a nested purchaseOrder.supplier.name and
    // item.material.name that the DTO never contains, so every row rendered
    // "Supplier record not expanded" and every line item "Material #1".
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    const table = screen.getByRole('table')
    expect(within(table).getByText('BuildWise Materials')).toBeInTheDocument()
    expect(within(table).getByText('Island Supply')).toBeInTheDocument()
    expect(screen.queryByText('Supplier record not expanded')).not.toBeInTheDocument()
    expect(screen.queryByText('Material #1')).not.toBeInTheDocument()
  })
})

// The record form and the detail view used to be permanent page sections, so the
// list, an always-open form and a full detail view were all on screen at once.
// Both are now opt-in drawers.
describe('DeliveriesPage (record form is opt-in)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    qualityApi.listDeliveries.mockResolvedValue(deliveries)
    qualityApi.listConfirmedPurchaseOrders.mockResolvedValue(orders)
    useAuth.mockReturnValue({ hasRole: () => true, user: { fullName: 'Site Officer' } })
  })

  it('hides the form until Record delivery is clicked', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Confirmed purchase order')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Record delivery' })).toBeInTheDocument()
  })

  it('opens the form in a drawer and closes it again', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    await user.click(screen.getByRole('button', { name: '+ Record delivery' }))

    const drawer = await screen.findByRole('dialog')
    expect(within(drawer).getByLabelText('Confirmed purchase order')).toBeInTheDocument()

    await user.click(within(drawer).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('closes the drawer on Escape', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    await user.click(screen.getByRole('button', { name: '+ Record delivery' }))
    await screen.findByRole('dialog')
    await user.keyboard('{Escape}')

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('does not offer the form to a view-only role', async () => {
    useAuth.mockReturnValue({ hasRole: () => false, user: { fullName: 'Quality Inspector' } })
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    expect(screen.queryByRole('button', { name: '+ Record delivery' })).not.toBeInTheDocument()
    expect(screen.getByText('View only')).toBeInTheDocument()
  })

  it('caps the received input at the ordered quantity', async () => {
    // Mirrors the backend rule Received <= Ordered, so an over-receipt is caught
    // in the browser instead of being submitted and silently accepted.
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    await user.click(screen.getByRole('button', { name: '+ Record delivery' }))
    const drawer = await screen.findByRole('dialog')
    await user.selectOptions(within(drawer).getByLabelText('Confirmed purchase order'), '5')

    // orders[0] has orderedQuantity 100.
    expect(within(drawer).getByRole('spinbutton', { name: 'Received' })).toHaveAttribute('max', '100')
  })

  it('submits every selected purchase-order line to the shared API', async () => {
    const user = userEvent.setup()
    qualityApi.recordDelivery.mockResolvedValue(deliveries[0])
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    await user.click(screen.getByRole('button', { name: '+ Record delivery' }))
    const drawer = await screen.findByRole('dialog')

    await user.selectOptions(within(drawer).getByLabelText('Confirmed purchase order'), '5')
    await user.type(within(drawer).getByRole('textbox', { name: /Delivery reference \/ invoice/ }), 'INV-3001')
    await user.type(within(drawer).getByRole('spinbutton', { name: 'Received' }), '95')
    await user.type(within(drawer).getByRole('spinbutton', { name: 'Damaged' }), '3')
    await user.click(within(drawer).getByRole('button', { name: 'Submit delivery entry' }))

    await waitFor(() => expect(qualityApi.recordDelivery).toHaveBeenCalledWith({
      purchaseOrderId: 5,
      deliveryReference: 'INV-3001',
      items: [{ materialId: 1, receivedQuantity: 95, damagedQuantity: 3 }],
    }))
  })
})

describe('DeliveriesPage (detail drawer)', () => {
  const analysisResult = {
    deliveryId: 22,
    agent: 'DeliveryDiscrepancyAgent',
    tool: 'analyze_discrepancy',
    executionSource: 'PythonDeliveryAgent',
    orderedQuantity: 100,
    receivedQuantity: 90,
    damagedQuantity: 5,
    shortageQuantity: 10,
    shortageDetected: true,
    damageDetected: true,
    summary: 'Discrepancy flagged.',
    recommendation: 'Review this delivery and the supplier response: short of 10 unit(s) and 5 unit(s) reported damaged.',
    deliveryStatus: 'DiscrepancyReported',
  }

  beforeEach(() => {
    vi.clearAllMocks()
    qualityApi.listDeliveries.mockResolvedValue(deliveries)
    qualityApi.listConfirmedPurchaseOrders.mockResolvedValue(orders)
    useAuth.mockReturnValue({ hasRole: () => true, user: { fullName: 'Site Officer' } })
  })

  // deliveries[] is ordered newest-first, so index 0 is #21 and index 1 is #22.
  async function openDetailFor(id) {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })
    await user.click(screen.getAllByRole('button', { name: 'View details' })[id === 22 ? 1 : 0])
    return { user, drawer: await screen.findByRole('dialog') }
  }

  it('opens no detail until a delivery is chosen', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    // The old page pre-selected the first delivery and always showed its detail.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByText('No delivery selected')).not.toBeInTheDocument()
  })

  it('shows the chosen delivery with its quantities and shortage', async () => {
    const { drawer } = await openDetailFor(22)

    // The reference shows twice inside the drawer: as the subtitle and as the
    // Information row, so assert with the *All* variant.
    expect(within(drawer).getAllByText('INV-2002').length).toBeGreaterThan(0)
    // Scoped to the detail columns: "Shortage" is also a row in the agent card.
    const infoCard = within(drawer).getByRole('heading', { name: 'Information' }).closest('.detail-columns')
    expect(within(infoCard).getByText('Ordered quantity').parentElement).toHaveTextContent('100')
    expect(within(infoCard).getByText('Received quantity').parentElement).toHaveTextContent('90')
    expect(within(infoCard).getByText('Damaged quantity').parentElement).toHaveTextContent('5')
    expect(within(infoCard).getByText('Shortage').parentElement).toHaveTextContent('10')
  })

  it('flags an impossible over-receipt instead of a contradictory "Shortage: None"', async () => {
    // A record stored before the backend enforced Received <= Ordered shows
    // "Shortage: None" beside a "Discrepancy Reported" status, which reads as a
    // contradiction. The anomaly is surfaced explicitly instead.
    qualityApi.listDeliveries.mockResolvedValue([{
      id: 23,
      purchaseOrderId: 4,
      deliveryReference: 'E2E-DELIVERY-PO4-001',
      status: 'DiscrepancyReported',
      deliveredAt: '2026-09-28T13:04:00Z',
      supplierName: 'Supplier A Building Materials',
      items: [{ id: 5, materialId: 1, materialName: 'Cement (50kg bag)', materialUnit: 'bag', orderedQuantity: 250, receivedQuantity: 500, damagedQuantity: 50 }],
    }])
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Delivery list' })

    await user.click(screen.getByRole('button', { name: 'View details' }))
    const drawer = await screen.findByRole('dialog')

    const infoCard = within(drawer).getByRole('heading', { name: 'Information' }).closest('.detail-columns')
    expect(within(infoCard).getByText('Over-receipt')).toBeInTheDocument()
    expect(within(infoCard).getByText(/250 more than ordered/)).toBeInTheDocument()
  })

  it('no longer offers the 30-option "Open another delivery" picker', async () => {
    const { drawer } = await openDetailFor(22)

    // The table is the single navigation source now.
    expect(within(drawer).queryByLabelText('Open another delivery')).not.toBeInTheDocument()
  })

  it('runs the discrepancy agent and renders its verdict', async () => {
    qualityApi.analyzeDeliveryDiscrepancy.mockResolvedValue(analysisResult)
    const { user, drawer } = await openDetailFor(22)

    await user.click(within(drawer).getByRole('button', { name: 'Run Delivery Analysis' }))

    expect(await within(drawer).findByText('DeliveryDiscrepancyAgent')).toBeInTheDocument()
    expect(within(drawer).getByText('analyze_discrepancy')).toBeInTheDocument()
    expect(within(drawer).getByText('PythonDeliveryAgent')).toBeInTheDocument()
    expect(within(drawer).getByText(/Review this delivery and the supplier response/)).toBeInTheDocument()
    expect(qualityApi.analyzeDeliveryDiscrepancy).toHaveBeenCalledWith(22)
  })

  it('reports a clean delivery as fully verified', async () => {
    qualityApi.analyzeDeliveryDiscrepancy.mockResolvedValue({
      ...analysisResult,
      deliveryId: 21,
      shortageQuantity: 0,
      shortageDetected: false,
      damageDetected: false,
      summary: 'Fully verified.',
      recommendation: 'No discrepancy. Proceed with standard receiving and quality inspection.',
    })
    const { user, drawer } = await openDetailFor(21)

    await user.click(within(drawer).getByRole('button', { name: 'Run Delivery Analysis' }))

    expect(await within(drawer).findByText('Fully verified.')).toBeInTheDocument()
    expect(qualityApi.analyzeDeliveryDiscrepancy).toHaveBeenCalledWith(21)
  })

  it('does not claim an analysis before the agent has been run', async () => {
    const { drawer } = await openDetailFor(22)

    expect(within(drawer).queryByText(/analysis completed/)).not.toBeInTheDocument()
    expect(within(drawer).getByRole('button', { name: 'Run Delivery Analysis' })).toBeInTheDocument()
  })

  it('keeps the delivery readable when the agent call fails', async () => {
    qualityApi.analyzeDeliveryDiscrepancy.mockRejectedValue(new Error('Delivery agent unavailable'))
    const { user, drawer } = await openDetailFor(22)

    await user.click(within(drawer).getByRole('button', { name: 'Run Delivery Analysis' }))

    expect(await within(drawer).findByText('Delivery agent unavailable')).toBeInTheDocument()
    expect(within(drawer).getAllByText('INV-2002').length).toBeGreaterThan(0)
  })

  it('clears a previous analysis when a different delivery is opened', async () => {
    qualityApi.analyzeDeliveryDiscrepancy.mockResolvedValue(analysisResult)
    const { user, drawer } = await openDetailFor(22)

    await user.click(within(drawer).getByRole('button', { name: 'Run Delivery Analysis' }))
    expect(await within(drawer).findByText('DeliveryDiscrepancyAgent')).toBeInTheDocument()

    await user.click(within(drawer).getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    // The next drawer must start clean, not show delivery 22's verdict.
    const { drawer: next } = await openDetailFor(21)
    expect(within(next).queryByText('DeliveryDiscrepancyAgent')).not.toBeInTheDocument()
  })
})


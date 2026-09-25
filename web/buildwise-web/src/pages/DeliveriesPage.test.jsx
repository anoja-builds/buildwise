import { render, screen, waitFor } from '@testing-library/react'
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
  },
}))

vi.mock('../auth/AuthContext', () => ({ useAuth: vi.fn() }))

const deliveries = [
  { id: 21, purchaseOrderId: 5, deliveryReference: 'INV-2001', status: 'Received', deliveredAt: '2026-09-24T08:00:00Z', purchaseOrder: { supplier: { name: 'BuildWise Materials' } }, items: [{ id: 1, materialId: 1, receivedQuantity: 100, damagedQuantity: 0, material: { name: 'Cement' } }] },
  { id: 22, purchaseOrderId: 6, deliveryReference: 'INV-2002', status: 'DiscrepancyReported', deliveredAt: '2026-09-23T08:00:00Z', purchaseOrder: { supplier: { name: 'Island Supply' } }, items: [{ id: 2, materialId: 1, receivedQuantity: 90, damagedQuantity: 5, material: { name: 'Cement' } }] },
]
const orders = [{ id: 5, expectedDeliveryDate: '2026-10-01', items: [{ id: 51, materialId: 1, orderedQuantity: 100, material: { name: 'Cement', unit: 'bags' } }] }, { id: 7, expectedDeliveryDate: '2026-10-03', items: [] }]

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

  it('shows the Component 3 pages and live dashboard metrics', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Component 3 pages' })).toBeInTheDocument()
    expect(screen.getByText('Attention required')).toBeInTheDocument()
    expect(screen.getByText((_, node) => node?.textContent === 'Delivery #21 · INV-2001')).toBeInTheDocument()
  })

  it('filters the persisted delivery list', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Dashboard' })
    await user.click(screen.getByRole('button', { name: 'List' }))
    await user.type(screen.getByLabelText('Search'), 'INV-2002')
    await waitFor(() => expect(screen.queryByText('INV-2001')).not.toBeInTheDocument())
    expect(screen.getByText('INV-2002')).toBeInTheDocument()
  })

  it('keeps the receiving form unavailable to view-only roles', async () => {
    useAuth.mockReturnValue({ hasRole: () => false, user: { fullName: 'Quality Inspector' } })
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Dashboard' })
    await user.click(screen.getByRole('button', { name: 'Form' }))
    expect(screen.getByText('Receiving is restricted')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit delivery entry' })).not.toBeInTheDocument()
  })

  it('submits every selected purchase-order line to the shared API', async () => {
    const user = userEvent.setup()
    qualityApi.recordDelivery.mockResolvedValue(deliveries[0])
    renderPage()
    await screen.findByRole('heading', { name: 'Dashboard' })
    await user.click(screen.getByRole('button', { name: 'Form' }))
    await user.selectOptions(screen.getByLabelText('Confirmed purchase order'), '5')
    await user.type(screen.getByRole('textbox', { name: /Delivery reference \/ invoice/ }), 'INV-3001')
    await user.type(screen.getByRole('spinbutton', { name: 'Received' }), '95')
    await user.type(screen.getByRole('spinbutton', { name: 'Damaged' }), '3')
    await user.click(screen.getByRole('button', { name: 'Submit delivery entry' }))
    await waitFor(() => expect(qualityApi.recordDelivery).toHaveBeenCalledWith({
      purchaseOrderId: 5,
      deliveryReference: 'INV-3001',
      items: [{ materialId: 1, receivedQuantity: 95, damagedQuantity: 3 }],
    }))
  })
})

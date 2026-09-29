import { render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import ProcurementApp from './ProcurementApp'
import { useAuth } from '../../../auth/AuthContext'
import { procurementApi } from '../services/procurementApi'

vi.mock('../../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('../services/procurementApi', () => ({ procurementApi: { getPurchaseOrder: vi.fn() } }))
beforeEach(() => {
  vi.clearAllMocks()
  procurementApi.getPurchaseOrder.mockResolvedValue({ id: 42, supplierName: 'Supplier', status: 'Confirmed', totalAmount: 100, items: [] })
})
it.each([['ProcurementManager', true], ['ProcurementOfficer', true], ['Administrator', true]])('%s purchase-order actions match PATCH authorization', async (role, editable) => {
  useAuth.mockReturnValue({ hasRole: (name) => name === role })
  render(<ProcurementApp section="Purchase Orders" orderId="42" onNavigate={vi.fn()} />)
  await screen.findByRole('heading', { name: 'Order information' })
  expect(Boolean(screen.queryByRole('heading', { name: 'Update status' }))).toBe(editable)
})

it.each(['SiteEngineer', 'QualityInspector', 'ReceivingOfficer', 'ProjectManager'])('blocks %s before loading PO details', role => {
  useAuth.mockReturnValue({ hasRole: name => name === role })
  render(<ProcurementApp section="Purchase Orders" orderId="42" onNavigate={vi.fn()} />)
  expect(screen.getByText(/does not have access/)).toBeInTheDocument()
  expect(procurementApi.getPurchaseOrder).not.toHaveBeenCalled()
})

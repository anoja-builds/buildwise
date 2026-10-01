import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import RequestWorkspace from './RequestWorkspace'
import { procurementApi } from '../services/procurementApi'
vi.mock('../services/procurementApi', () => ({ procurementApi: {
  getMaterialRequest: vi.fn(), compareQuotations: vi.fn(), getLatestWorkflow: vi.fn(), listSuppliers: vi.fn(),
  recordDecision: vi.fn(), getWorkflow: vi.fn(), getPurchaseOrder: vi.fn(), createPurchaseOrderFromWorkflow: vi.fn()
} }))
const workflow = { id: 9, status: 'AwaitingApproval', approvalStatus: 'Pending', steps: [],
  recommendation: { recommendedSupplierId: 2, recommendedQuotationId: 3, recommendedSupplierName: 'Live supplier',
    rationale: 'Full coverage', executionMode: 'PythonDeterministicFallback', rankedAlternatives: [], warnings: [] }, validation: { isValid: true } }
beforeEach(() => {
  vi.clearAllMocks()
  procurementApi.getMaterialRequest.mockResolvedValue({ id: 5, projectName: 'Project', status: 'Approved', items: [] })
  procurementApi.compareQuotations.mockResolvedValue({ rows: [], quotations: [] })
  procurementApi.getLatestWorkflow.mockResolvedValue(workflow)
  procurementApi.listSuppliers.mockResolvedValue({ items: [] })
})
it('restores the persisted workflow and uses the PO returned by approval without creating it twice', async () => {
  procurementApi.recordDecision.mockResolvedValue({ purchaseOrderId: 17 })
  procurementApi.getWorkflow.mockResolvedValue({ ...workflow, purchaseOrderId: 17, status: 'Completed', approvalStatus: 'Approved' })
  procurementApi.getPurchaseOrder.mockResolvedValue({ id: 17, supplierName: 'Live supplier' })
  const user = userEvent.setup(); const onView = vi.fn()
  render(<RequestWorkspace requestId={5} role="Manager" onViewPurchaseOrder={onView} />)
  await user.click(await screen.findByRole('button', { name: 'Comparison & AI Recommendation' }))
  expect(screen.getByText(/deterministic rationale; AI advice unavailable/)).toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Approve', exact: true }))
  await waitFor(() => expect(onView).toHaveBeenCalledWith(17))
  expect(procurementApi.createPurchaseOrderFromWorkflow).not.toHaveBeenCalled()
})
it('officers can read the persisted recommendation but cannot approve', async () => {
  const user = userEvent.setup()
  render(<RequestWorkspace requestId={5} role="Officer" />)
  await user.click(await screen.findByRole('button', { name: 'Comparison & AI Recommendation' }))
  expect(screen.queryByRole('button', { name: 'Approve', exact: true })).not.toBeInTheDocument()
  expect(screen.getByText(/Officers can view status only/)).toBeInTheDocument()
})
it('shows a workflow load failure instead of an invented recommendation', async () => {
  procurementApi.getLatestWorkflow.mockRejectedValue(new Error('Workflow API unavailable'))
  render(<RequestWorkspace requestId={5} role="Manager" />)
  expect(await screen.findByText('Workflow API unavailable')).toBeInTheDocument()
  expect(screen.queryByText('Live supplier')).not.toBeInTheDocument()
})

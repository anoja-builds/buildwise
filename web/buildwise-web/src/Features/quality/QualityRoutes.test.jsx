import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { RoutedQualityApp } from './QualityApp'
import { qualityApi } from './services/qualityApi'

vi.mock('./QualityRiskPanel', () => ({ default: () => null }))
vi.mock('./services/qualityApi', () => ({ qualityApi: {
  pendingDeliveries: vi.fn(), getInspection: vi.fn(), startInspection: vi.fn(), listInspections: vi.fn(), listNcrs: vi.fn(),
} }))
const inspection = { id: 7, status: 'UnderInspection', items: [], deliveryItems: [{ deliveryItemId: 101, receivedQuantity: 50 }] }
function Location() { return <output aria-label="URL">{useLocation().pathname}</output> }
function mount(path) {
  render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/quality-inspections" element={<RoutedQualityApp kind="inspectionList" />} />
    <Route path="/quality-inspections/:id" element={<RoutedQualityApp kind="inspection" />} />
    <Route path="/quality-inspections/new/:deliveryId" element={<RoutedQualityApp kind="startInspection" />} />
    <Route path="/quality-inspections/:id/complete" element={<RoutedQualityApp kind="completeInspection" />} />
    <Route path="/quality-inspections/:id/non-conformances/new/:itemId" element={<RoutedQualityApp kind="create" />} />
  </Routes><Location /></MemoryRouter>)
}
beforeEach(() => {
  vi.resetAllMocks()
  qualityApi.pendingDeliveries.mockResolvedValue([{ deliveryId: 10, deliveryReference: 'DEL-10', items: [] }])
  qualityApi.getInspection.mockResolvedValue(inspection)
  qualityApi.startInspection.mockResolvedValue(inspection)
  qualityApi.listInspections.mockResolvedValue([])
  qualityApi.listNcrs.mockResolvedValue([])
})
it('reloads a start form by delivery ID and navigates to the saved inspection completion URL', async () => {
  mount('/quality-inspections/new/10')
  await screen.findByRole('heading', { name: /Start Inspection/ })
  fireEvent.click(screen.getByRole('button', { name: 'Start Inspection', exact: true }))
  await screen.findByRole('heading', { name: /Complete Inspection/ })
  expect(screen.getByLabelText('URL')).toHaveTextContent('/quality-inspections/7/complete')
  expect(qualityApi.getInspection).toHaveBeenCalledWith('7')
})
it('loads a completion form directly without in-memory navigation state', async () => {
  mount('/quality-inspections/7/complete')
  await screen.findByRole('heading', { name: /Complete Inspection/ })
  expect(qualityApi.getInspection).toHaveBeenCalledWith('7')
})
it('reloads the inspection and item for an NCR form, then cancels to inspection detail', async () => {
  qualityApi.getInspection.mockResolvedValue({ ...inspection, status: 'Completed', items: [{ id: 9, rejectedQuantity: 5 }] })
  mount('/quality-inspections/7/non-conformances/new/9')
  await screen.findByRole('heading', { name: 'Create non-conformance' })
  fireEvent.click(screen.getByRole('button', { name: 'Cancel', exact: true }))
  await screen.findByRole('heading', { name: 'Inspection #7' })
  expect(screen.getByLabelText('URL')).toHaveTextContent('/quality-inspections/7')
})
it('shows a recoverable error when the delivery is no longer pending', async () => {
  qualityApi.pendingDeliveries.mockResolvedValue([])
  mount('/quality-inspections/new/10')
  expect(await screen.findByRole('alert')).toHaveTextContent('no longer pending')
  expect(screen.queryByRole('button', { name: 'Start Inspection', exact: true })).not.toBeInTheDocument()
})
it.each(['Accepted', 'PartiallyAccepted', 'Rejected'])('keeps a completed %s inspection read-only at the direct completion URL', async (overallDecision) => {
  qualityApi.getInspection.mockResolvedValue({ ...inspection, status: 'Completed', overallDecision })
  mount('/quality-inspections/7/complete')
  await screen.findByRole('heading', { name: 'Inspection #7' })
  expect(await screen.findByText(overallDecision)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Complete inspection/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument()
})
it('does not allow a new inspection when the server excludes an accepted delivery', async () => {
  qualityApi.pendingDeliveries.mockResolvedValue([])
  mount('/quality-inspections/new/10')
  expect(await screen.findByRole('alert')).toHaveTextContent('no longer pending')
  expect(qualityApi.startInspection).not.toHaveBeenCalled()
})
it('offers re-inspection only when the server returns the delivery as eligible', async () => {
  mount('/quality-inspections/new/10')
  expect(await screen.findByRole('button', { name: 'Start Inspection', exact: true })).toBeEnabled()
  expect(qualityApi.pendingDeliveries).toHaveBeenCalled()
})

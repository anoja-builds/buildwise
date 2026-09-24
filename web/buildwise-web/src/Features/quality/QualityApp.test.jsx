import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import QualityApp from './QualityApp'
import { useAuth } from '../../auth/AuthContext'
import { qualityApi } from './services/qualityApi'
vi.mock('../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('./services/qualityApi', () => ({ qualityApi: Object.fromEntries(['listInspections', 'getInspection', 'listNcrs', 'getNcr', 'createNcr', 'updateCorrectiveAction', 'resolveNcr', 'closeNcr'].map((key) => [key, vi.fn()])) }))
const inspection = { id: 1, deliveryId: 2, deliveryReference: 'DEL-240', inspectorUserId: 7, inspectorName: 'Inspector Silva', status: 'Completed', overallDecision: 'PartiallyAccepted', items: [{ id: 9, deliveryItemId: 3, condition: 'Damaged bags', acceptedQuantity: 235, rejectedQuantity: 5 }, { id: 10, deliveryItemId: 4, acceptedQuantity: 10, rejectedQuantity: 0 }], deliveryItems: [{ deliveryItemId: 3, receivedQuantity: 240 }] }
const ncr = { id: 5, inspectionId: 1, inspectionItemId: 9, issueDescription: 'Wet cement', severity: 'High', status: 'Open', rejectedQuantity: 5 }
beforeEach(() => {
  vi.resetAllMocks(); useAuth.mockReturnValue({ roles: ['QualityInspector'] })
  qualityApi.listInspections.mockResolvedValue([inspection]); qualityApi.getInspection.mockResolvedValue(inspection)
  qualityApi.listNcrs.mockResolvedValue([ncr]); qualityApi.getNcr.mockResolvedValue(ncr)
})
const renderInspections = () => render(<QualityApp section="Quality Inspections" />)
const openInspection = async () => { fireEvent.click(await screen.findByRole('button', { name: 'Inspection #1' })); await screen.findByText('Damaged bags') }
const openNcr = async () => { render(<QualityApp section="Non-Conformances" />); fireEvent.click(await screen.findByRole('button', { name: 'NCR #5' })); await screen.findByText('Non-conformance details') }

it.each(['ProcurementOfficer', 'ReceivingOfficer', 'ProjectManager'])('blocks %s before reading or mutating', (role) => {
  useAuth.mockReturnValue({ roles: [role] }); renderInspections()
  expect(screen.getByText('Access restricted')).toBeInTheDocument(); expect(qualityApi.listInspections).not.toHaveBeenCalled()
})
it('allows administrators and displays inspection evidence', async () => {
  useAuth.mockReturnValue({ roles: ['Administrator'] }); renderInspections(); await openInspection()
  expect(screen.getByText('Inspector Silva')).toBeInTheDocument(); expect(screen.getByText('235')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Create NCR for item #9' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Create NCR for item #10' })).not.toBeInTheDocument()
})
it('does not offer NCR creation for unfinished inspections', async () => {
  qualityApi.getInspection.mockResolvedValue({ ...inspection, status: 'UnderInspection' }); renderInspections(); await openInspection()
  expect(screen.queryByRole('button', { name: /Create NCR/ })).not.toBeInTheDocument()
})
it('shows loading, empty and retryable network error states', async () => {
  qualityApi.listInspections.mockRejectedValueOnce(new Error('Network unavailable')).mockResolvedValueOnce([])
  renderInspections(); expect(screen.getByText('Loading quality records...')).toBeInTheDocument()
  await screen.findByText('Network unavailable'); fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  expect(await screen.findByText('No inspections yet')).toBeInTheDocument()
})
it('requires human input and explicitly submits the selected rejected item', async () => {
  qualityApi.createNcr.mockResolvedValue(ncr); renderInspections(); await openInspection()
  fireEvent.click(screen.getByRole('button', { name: 'Create NCR for item #9' }))
  expect(qualityApi.createNcr).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText(/Issue description/), { target: { value: '   ' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create NCR' })); await screen.findByText('Enter an issue description.')
  expect(qualityApi.createNcr).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText(/Issue description/), { target: { value: 'Wet cement' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create NCR' }))
  await waitFor(() => expect(qualityApi.createNcr).toHaveBeenCalledExactlyOnceWith({ inspectionItemId: 9, issueDescription: 'Wet cement', severity: 'Medium', correctiveAction: null }))
  await screen.findByText('Non-conformance details')
})
it('saves corrective actions and blocks resolution until one is saved', async () => {
  await openNcr(); expect(screen.queryByRole('button', { name: 'Mark resolved' })).not.toBeInTheDocument()
  qualityApi.updateCorrectiveAction.mockResolvedValue({ ...ncr, correctiveAction: 'Replace bags' })
  fireEvent.change(screen.getByLabelText(/Corrective action/), { target: { value: 'Replace bags' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save corrective action' }))
  await waitFor(() => expect(qualityApi.updateCorrectiveAction).toHaveBeenCalledWith(5, 'Replace bags'))
})
it.each(['Resolved', 'Closed'])('hides editing for %s NCRs', async (status) => {
  qualityApi.getNcr.mockResolvedValue({ ...ncr, status }); await openNcr()
  expect(screen.queryByRole('button', { name: 'Save corrective action' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Mark resolved' })).not.toBeInTheDocument()
  if (status === 'Resolved') { fireEvent.click(screen.getByRole('button', { name: 'Close NCR' })); await waitFor(() => expect(qualityApi.closeNcr).toHaveBeenCalledWith(5)) }
  else expect(screen.queryByRole('button', { name: 'Close NCR' })).not.toBeInTheDocument()
})
it('reports conflicting transitions without claiming success', async () => {
  qualityApi.getNcr.mockResolvedValue({ ...ncr, status: 'CorrectiveActionRequired', correctiveAction: 'Replace' })
  qualityApi.resolveNcr.mockRejectedValue(new Error('Non-conformance has already been resolved.'))
  await openNcr(); fireEvent.click(screen.getByRole('button', { name: 'Mark resolved' }))
  expect(await screen.findByText('Non-conformance has already been resolved.')).toBeInTheDocument()
})

it('refreshes after explicit resolution and then closure', async () => {
  qualityApi.getNcr.mockResolvedValueOnce({ ...ncr, status: 'CorrectiveActionRequired', correctiveAction: 'Replace' })
    .mockResolvedValueOnce({ ...ncr, status: 'Resolved', correctiveAction: 'Replace', resolvedAt: '2026-09-24T12:00:00Z' })
    .mockResolvedValueOnce({ ...ncr, status: 'Closed', correctiveAction: 'Replace', resolvedAt: '2026-09-24T12:00:00Z' })
  qualityApi.resolveNcr.mockResolvedValue({}); qualityApi.closeNcr.mockResolvedValue({})
  await openNcr(); fireEvent.click(screen.getByRole('button', { name: 'Mark resolved' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Close NCR' }))
  await screen.findByText('Closed')
  expect(qualityApi.resolveNcr).toHaveBeenCalledExactlyOnceWith(5)
  expect(qualityApi.closeNcr).toHaveBeenCalledExactlyOnceWith(5)
  expect(screen.queryByRole('button', { name: 'Close NCR' })).not.toBeInTheDocument()
})

it('does not navigate back to a saved NCR after the reviewer has left its form', async () => {
  let finish
  qualityApi.createNcr.mockReturnValue(new Promise((resolve) => { finish = resolve }))
  renderInspections(); await openInspection()
  fireEvent.click(screen.getByRole('button', { name: 'Create NCR for item #9' }))
  fireEvent.change(screen.getByLabelText(/Issue description/), { target: { value: 'Wet bags' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create NCR' }))
  fireEvent.click(screen.getByRole('button', { name: 'Back to Quality Inspections' }))
  await screen.findByRole('button', { name: 'Inspection #1' })
  finish(ncr)
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Quality Inspections' })).toBeInTheDocument())
  expect(qualityApi.getNcr).not.toHaveBeenCalled()
})

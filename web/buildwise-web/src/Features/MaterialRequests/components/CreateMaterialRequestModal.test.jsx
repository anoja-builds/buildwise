import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import CreateMaterialRequestModal from './CreateMaterialRequestModal'
import { materialRequestService } from '../services/materialRequestService'
vi.mock('../services/materialRequestService', () => ({ materialRequestService: { getOptions: vi.fn(), createRequest: vi.fn() } }))
beforeEach(() => vi.resetAllMocks())
it('uses API options and units without sending a requester identity', async () => {
  materialRequestService.getOptions.mockResolvedValue({ projects: [{ id: 87, name: 'Actual project' }], materials: [{ id: 93, name: 'Actual material', unit: 'kg' }] })
  materialRequestService.createRequest.mockResolvedValue({ id: 44 })
  const saved = vi.fn()
  render(<CreateMaterialRequestModal onSuccess={saved} />)
  fireEvent.change(await screen.findByLabelText('Project'), { target: { value: '87' } })
  fireEvent.change(screen.getByLabelText('Material 1'), { target: { value: '93' } })
  fireEvent.change(screen.getByLabelText(/^Quantity/), { target: { value: '12' } })
  fireEvent.change(screen.getByLabelText(/^Required Date/), { target: { value: '2026-10-10' } })
  fireEvent.change(screen.getByLabelText(/^Reason \/ Justification/), { target: { value: 'Site work' } })
  expect(screen.getByLabelText('Unit')).toHaveValue('kg')
  fireEvent.click(screen.getByRole('button', { name: 'Submit for Approval' }))
  await waitFor(() => expect(saved).toHaveBeenCalled())
  expect(materialRequestService.createRequest).toHaveBeenCalledWith({ projectId: 87, requiredDate: '2026-10-10T00:00:00.000Z', reason: 'Site work', submitImmediately: true, items: [{ materialId: 93, quantity: 12, unit: 'kg', notes: null }] })
})
it('shows loading without selectable demo values', () => {
  materialRequestService.getOptions.mockReturnValue(new Promise(() => {}))
  render(<CreateMaterialRequestModal />)
  expect(screen.getByText('Loading projects and materials...')).toBeInTheDocument()
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
})
it('offers retry on error and handles empty options', async () => {
  materialRequestService.getOptions.mockRejectedValueOnce(new Error('Options unavailable')).mockResolvedValueOnce({ projects: [], materials: [] })
  render(<CreateMaterialRequestModal />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Options unavailable')
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  expect(await screen.findByText('No request options available')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Submit for Approval' })).not.toBeInTheDocument()
})

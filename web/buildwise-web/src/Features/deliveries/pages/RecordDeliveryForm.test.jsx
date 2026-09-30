import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import RecordDeliveryForm from './RecordDeliveryForm'
import { deliveryService } from '../services/deliveryService'
vi.mock('../services/deliveryService', () => ({ deliveryService: { receiveDelivery: vi.fn(), saveEvidenceLink: vi.fn() } }))
const delivery = { id: 12, deliveryReference: 'DEL-12', status: 'Scheduled', items: [{ purchaseOrderItemId: 29, materialName: 'Cement', orderedQuantity: 100, outstandingQuantity: 60, materialUnit: 'bags' }] }
beforeEach(() => vi.resetAllMocks())
it('validates against outstanding quantities and previews usable quantity', () => {
  render(<RecordDeliveryForm delivery={delivery} />)
  const received = screen.getByLabelText('Received quantity 1')
  const damaged = screen.getByLabelText('Damaged quantity 1')
  expect(received).toHaveValue(60)
  fireEvent.change(received, { target: { value: '61' } })
  expect(screen.getByRole('button', { name: 'Confirm Receipt' })).toBeDisabled()
  fireEvent.change(received, { target: { value: '50' } })
  fireEvent.change(damaged, { target: { value: '51' } })
  expect(screen.getByRole('button', { name: 'Confirm Receipt' })).toBeDisabled()
  fireEvent.change(damaged, { target: { value: '5' } })
  expect(screen.getByLabelText('Usable quantity 1')).toHaveTextContent('45')
  expect(screen.getByRole('button', { name: 'Confirm Receipt' })).toBeEnabled()
})
it('does not invent evidence and does not repeat receiving when evidence metadata fails', async () => {
  deliveryService.receiveDelivery.mockResolvedValue({ status: 'Received' })
  deliveryService.saveEvidenceLink.mockRejectedValueOnce(new Error('Link could not be saved')).mockResolvedValueOnce({})
  const success = vi.fn()
  render(<RecordDeliveryForm delivery={delivery} onSuccess={success} />)
  expect(screen.queryByText(/Attach Photo/)).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Existing evidence link (optional)'), { target: { value: 'https://example.org/evidence.jpg' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Receipt' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Link could not be saved')
  expect(success).not.toHaveBeenCalled()
  expect(screen.getByLabelText('Received quantity 1')).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Retry evidence link' }))
  await waitFor(() => expect(success).toHaveBeenCalledTimes(1))
  expect(deliveryService.receiveDelivery).toHaveBeenCalledTimes(1)
  expect(deliveryService.saveEvidenceLink).toHaveBeenLastCalledWith(12, 'https://example.org/evidence.jpg')
})

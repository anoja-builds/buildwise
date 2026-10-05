import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import QuotationEntryForm from './QuotationEntryForm'

const requestDetail = {
  id: 101,
  status: 'Approved',
  items: [{ id: 1001, materialName: 'Cement (50kg bag)', unit: 'bag', requestedQuantity: 250 }]
}

describe('QuotationEntryForm validation', () => {
  it('requires a supplier before it will submit', async () => {
    const user = userEvent.setup()
    render(<QuotationEntryForm requestDetail={requestDetail} onCreated={() => {}} />)

    await user.click(screen.getByRole('button', { name: 'Save quotation' }))

    expect(await screen.findByText('Select a supplier.')).toBeInTheDocument()
  })

  it('requires at least one line item with a quantity and price', async () => {
    const user = userEvent.setup()
    render(<QuotationEntryForm requestDetail={requestDetail} onCreated={() => {}} />)

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Supplier' }).options.length).toBeGreaterThan(1))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Supplier' }), '1')
    await user.click(screen.getByRole('button', { name: 'Save quotation' }))

    expect(await screen.findByText('Enter quantity and unit price for at least one item.')).toBeInTheDocument()
  })

  it('rejects a zero quantity even when a price is entered', async () => {
    // A negative value is blocked earlier by the field's own min="0" HTML5
    // constraint (the browser never fires the submit event at all), so this
    // exercises the app's own "quantity must be positive" business rule via
    // the one value (0) that constraint validation lets through.
    const user = userEvent.setup()
    render(<QuotationEntryForm requestDetail={requestDetail} onCreated={() => {}} />)

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Supplier' }).options.length).toBeGreaterThan(1))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Supplier' }), '1')
    await user.type(screen.getByLabelText('Quantity (bag)'), '0')
    await user.type(screen.getByLabelText('Unit price'), '2100')
    await user.click(screen.getByRole('button', { name: 'Save quotation' }))

    expect(await screen.findByText('Quantity must be positive and unit price cannot be negative.')).toBeInTheDocument()
  })

  it('computes the live line total and quotation total as quantity/price are entered', async () => {
    const user = userEvent.setup()
    render(<QuotationEntryForm requestDetail={requestDetail} onCreated={() => {}} />)

    await user.type(screen.getByLabelText('Quantity (bag)'), '250')
    await user.type(screen.getByLabelText('Unit price'), '2100')

    expect(await screen.findAllByText('525,000')).toHaveLength(2)
  })
})

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import SupplierFormModal, { validateSupplierForm } from './SupplierFormModal'

function renderModal(onSubmit = vi.fn()) {
  render(
    <SupplierFormModal open onCancel={() => {}} onSubmit={onSubmit} submitting={false} />,
  )
  return onSubmit
}

describe('SupplierFormModal validation', () => {
  it('requires a supplier name', async () => {
    const user = userEvent.setup()
    const onSubmit = renderModal()

    await user.click(screen.getByRole('button', { name: 'Add supplier' }))

    expect(await screen.findByText('Supplier name is required.')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('rejects a name made only of numbers', async () => {
    const user = userEvent.setup()
    const onSubmit = renderModal()

    await user.type(screen.getByLabelText(/Supplier name/), '12345')
    await user.click(screen.getByRole('button', { name: 'Add supplier' }))

    expect(
      await screen.findByText('Supplier name must include letters — it cannot be only numbers or symbols.'),
    ).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('rejects a contact person containing digits', async () => {
    const user = userEvent.setup()
    const onSubmit = renderModal()

    await user.type(screen.getByLabelText(/Supplier name/), 'Supplier A')
    await user.type(screen.getByLabelText(/Contact person/), 'Priya 007')
    await user.click(screen.getByRole('button', { name: 'Add supplier' }))

    expect(
      await screen.findByText(/Contact person must contain letters only/),
    ).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('rejects a malformed email and points at the gmail example', async () => {
    const user = userEvent.setup()
    const onSubmit = renderModal()

    await user.type(screen.getByLabelText(/Supplier name/), 'Supplier A')
    await user.type(screen.getByLabelText(/Email/), 'sales@supplierade')
    await user.click(screen.getByRole('button', { name: 'Add supplier' }))

    expect(
      await screen.findByText('Please enter a valid email address (e.g. supplier@gmail.com).'),
    ).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('rejects a phone that is not a Sri Lankan mobile number', async () => {
    const user = userEvent.setup()
    const onSubmit = renderModal()

    await user.type(screen.getByLabelText(/Supplier name/), 'Supplier A')
    await user.type(screen.getByLabelText(/Phone/), '5551234')
    await user.click(screen.getByRole('button', { name: 'Add supplier' }))

    expect(
      await screen.findByText(/valid 10-digit Sri Lankan mobile number/),
    ).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('accepts a fully valid supplier and normalises the phone number', async () => {
    const user = userEvent.setup()
    const onSubmit = renderModal()

    await user.type(screen.getByLabelText(/Supplier name/), 'Supplier A (Pvt) Ltd')
    await user.type(screen.getByLabelText(/Contact person/), 'Priya Officer')
    await user.type(screen.getByLabelText(/Email/), 'salesdemo@gmail.com')
    await user.type(screen.getByLabelText(/Phone/), '0771234567')
    await user.type(screen.getByLabelText(/Address/), '12 Galle Rd, Colombo 03')
    await user.click(screen.getByRole('button', { name: 'Add supplier' }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Supplier A (Pvt) Ltd',
        contactPerson: 'Priya Officer',
        email: 'salesdemo@gmail.com',
        phone: '077 123 4567',
        address: '12 Galle Rd, Colombo 03',
      }),
    )
  })
})

describe('validateSupplierForm rules', () => {
  const valid = {
    name: 'Supplier B',
    contactPerson: 'Jane Doe',
    email: 'jane@gmail.com',
    phone: '0751234567',
    address: 'No. 4, Kandy Rd',
  }

  it('passes a clean record with no field errors', () => {
    const { errors } = validateSupplierForm(valid)
    expect(errors).toEqual({})
  })

  it('flags an address made only of symbols', () => {
    const { errors } = validateSupplierForm({ ...valid, address: '###' })
    expect(errors.address).toBe('Address must contain letters, numbers and symbols only.')
  })

  it('flags an email without a domain', () => {
    const { errors } = validateSupplierForm({ ...valid, email: 'not-an-email@' })
    expect(errors.email).toContain('supplier@gmail.com')
  })

  it('allows an empty optional field but not an invalid one', () => {
    expect(validateSupplierForm({ ...valid, contactPerson: '', email: '', phone: '', address: '' }).errors).toEqual({})
    expect(validateSupplierForm({ ...valid, contactPerson: 'J0hn' }).errors.contactPerson).toBeDefined()
  })
})
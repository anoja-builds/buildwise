import { useEffect, useState } from 'react'
import { Button, TextInput } from '../../../components/shared'

const emptyForm = { name: '', contactPerson: '', email: '', phone: '', address: '' }

export default function SupplierFormModal({ open, supplier, onCancel, onSubmit, submitting }) {
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState('')

  useEffect(() => {
    if (open) {
      setForm(supplier ? { name: supplier.name, contactPerson: supplier.contactPerson || '', email: supplier.email || '', phone: supplier.phone || '', address: supplier.address || '' } : emptyForm)
      setError('')
    }
  }, [open, supplier])

  if (!open) return null

  const update = (field) => (event) => setForm((prev) => ({ ...prev, [field]: event.target.value }))

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!form.name.trim()) {
      setError('Supplier name is required.')
      return
    }
    setError('')
    await onSubmit(form)
  }

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-modal-title" style={{ maxWidth: 480, width: '100%' }}>
        <h2 id="supplier-modal-title">{supplier ? 'Edit supplier' : 'Add supplier'}</h2>
        <form className="form-grid" onSubmit={handleSubmit} style={{ marginTop: 'var(--space-4)' }}>
          <div className="form-span">
            <TextInput label="Supplier name" name="name" value={form.name} onChange={update('name')} required error={error || undefined} />
          </div>
          <TextInput label="Contact person" name="contactPerson" value={form.contactPerson} onChange={update('contactPerson')} />
          <TextInput label="Email" name="email" type="email" value={form.email} onChange={update('email')} />
          <TextInput label="Phone" name="phone" value={form.phone} onChange={update('phone')} />
          <div className="form-span">
            <TextInput label="Address" name="address" value={form.address} onChange={update('address')} multiline />
          </div>
          <div className="form-actions form-span">
            <Button type="button" variant="secondary" onClick={onCancel}>Cancel</Button>
            <Button type="submit" disabled={submitting}>{submitting ? 'Saving…' : supplier ? 'Save changes' : 'Add supplier'}</Button>
          </div>
        </form>
      </div>
    </div>
  )
}

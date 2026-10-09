import { useEffect, useState } from 'react'
import { Button, TextInput } from '../../../components/shared'
import { validateSriLankanMobile, isValidEmail } from '../../../utils/sriLankaValidation'

const emptyForm = { name: '', contactPerson: '', email: '', phone: '', address: '' }

// Supplier name — a business label: letters are required, and numbers plus
// basic business symbols are allowed. A name made only of digits is not a name.
const NAME_ALLOWED = /^[\p{L}\p{N}][\p{L}\p{N}\s&.'\-/()]*$/u
// Contact person — a person's name is letters, optionally joined by spaces,
// hyphens, apostrophes and the dot of an initial ("A. Perera", "Priya Officer").
const CONTACT_ALLOWED = /^\p{L}[\p{L}\s.'-]*$/u
// Address — letters, numbers and the symbols an address actually uses.
const ADDRESS_ALLOWED = /^[\p{L}\p{N}\s,.\-/#()&']+$/u

/**
 * Field rules for the supplier form (mirrored by the mobile supplier screen):
 *   name           — required text (letters must appear; digits-only rejected)
 *   contact person — letters only, when provided
 *   email          — a real mail address (e.g. supplier@gmail.com), when provided
 *   phone          — TRCSL Sri Lankan mobile (07X XXX XXX / +947X XXX XXX)
 *   address        — letters, numbers and address symbols, when provided
 *
 * @returns {{ errors: Object, normalizedPhone: string }}
 */
export function validateSupplierForm(form) {
  const errors = {}

  const name = (form.name || '').trim()
  if (!name) {
    errors.name = 'Supplier name is required.'
  } else if (!/\p{L}/u.test(name)) {
    errors.name = 'Supplier name must include letters — it cannot be only numbers or symbols.'
  } else if (!NAME_ALLOWED.test(name)) {
    errors.name = 'Supplier name may only contain letters, numbers, spaces and basic symbols (&, ., -, /).'
  }

  const contactPerson = (form.contactPerson || '').trim()
  if (contactPerson && !CONTACT_ALLOWED.test(contactPerson)) {
    errors.contactPerson = 'Contact person must contain letters only (spaces, hyphens and apostrophes are allowed).'
  }

  const email = (form.email || '').trim()
  if (email && !isValidEmail(email)) {
    errors.email = 'Please enter a valid email address (e.g. supplier@gmail.com).'
  }

  let normalizedPhone = (form.phone || '').trim()
  if (normalizedPhone) {
    const phoneCheck = validateSriLankanMobile(normalizedPhone)
    if (!phoneCheck.isValid) {
      errors.phone = phoneCheck.error
    } else {
      normalizedPhone = phoneCheck.display || normalizedPhone
    }
  }

  const address = (form.address || '').trim()
  if (address && (!ADDRESS_ALLOWED.test(address) || !/[\p{L}\p{N}]/u.test(address))) {
    errors.address = 'Address must contain letters, numbers and symbols only.'
  }

  return { errors, normalizedPhone }
}

export default function SupplierFormModal({ open, supplier, onCancel, onSubmit, submitting }) {
  const [form, setForm] = useState(emptyForm)
  const [errors, setErrors] = useState({})

  useEffect(() => {
    if (open) {
      setForm(supplier ? { name: supplier.name, contactPerson: supplier.contactPerson || '', email: supplier.email || '', phone: supplier.phone || '', address: supplier.address || '' } : emptyForm)
      setErrors({})
    }
  }, [open, supplier])

  if (!open) return null

  const update = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
    // Clear the field's error as soon as the officer retypes in it, so a stale
    // message is never shown while the fix is in progress.
    setErrors((prev) => (prev[field] ? { ...prev, [field]: undefined } : prev))
  }

  const handleSubmit = async (event) => {
    event.preventDefault()
    const { errors: found, normalizedPhone } = validateSupplierForm(form)
    if (Object.values(found).some(Boolean)) {
      setErrors(found)
      return
    }
    setErrors({})
    await onSubmit({
      ...form,
      name: form.name.trim(),
      contactPerson: form.contactPerson.trim(),
      email: form.email.trim(),
      address: form.address.trim(),
      phone: normalizedPhone,
    })
  }

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-modal-title" style={{ maxWidth: 480, width: '100%' }}>
        <h2 id="supplier-modal-title">{supplier ? 'Edit supplier' : 'Add supplier'}</h2>
        <form className="form-grid" onSubmit={handleSubmit} style={{ marginTop: 'var(--space-4)' }}>
          <div className="form-span">
            <TextInput label="Supplier name" name="name" value={form.name} onChange={update('name')} aria-required error={errors.name} hint={errors.name ? undefined : 'Letters, numbers and basic symbols (e.g. Supplier A (Pvt) Ltd).'} />
          </div>
          <TextInput label="Contact person" name="contactPerson" value={form.contactPerson} onChange={update('contactPerson')} error={errors.contactPerson} hint={errors.contactPerson ? undefined : 'Letters only (e.g. Priya Officer).'} />
          <TextInput label="Email" name="email" type="email" value={form.email} onChange={update('email')} error={errors.email} hint={errors.email ? undefined : 'e.g. supplier@gmail.com'} />
          <TextInput label="Phone" name="phone" value={form.phone} onChange={update('phone')} error={errors.phone} hint={errors.phone ? undefined : 'Sri Lankan mobile, e.g. 0771234567.'} />
          <div className="form-span">
            <TextInput label="Address" name="address" value={form.address} onChange={update('address')} multiline error={errors.address} hint={errors.address ? undefined : 'Letters, numbers and symbols (e.g. 12 Galle Rd, Colombo 03).'} />
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

import { useEffect, useState } from 'react'
import { Button, Card, PageHeader, SelectInput, StatusBadge, TextInput } from '../../components/shared'
import { useAuth } from '../../auth/AuthContext'
import { usersApi } from './usersApi'

const roles = [
  { value: 'SiteEngineer', label: 'Site Engineer' },
  { value: 'ProcurementOfficer', label: 'Procurement Officer' },
  { value: 'ProcurementManager', label: 'Procurement Manager' },
  { value: 'QualityInspector', label: 'Quality Inspector' },
  { value: 'Administrator', label: 'Administrator' },
]
const emptyForm = { fullName: '', email: '', password: '', roleName: 'SiteEngineer' }

export default function UsersPage() {
  const { user: actor } = useAuth()
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [form, setForm] = useState(null)
  const [busy, setBusy] = useState(false)

  async function load() {
    setLoading(true)
    setError('')
    try { setUsers(await usersApi.list()) }
    catch (err) { setError(err.message) }
    finally { setLoading(false) }
  }
  useEffect(() => { load() }, [])

  const update = (field) => (event) => setForm({ ...form, [field]: event.target.value })
  async function create(event) {
    event.preventDefault()
    if (!form.fullName.trim() || !form.email.trim() || !form.password.trim() || form.password.length < 8) {
      setError('Enter a name, email, and password of at least 8 characters.')
      return
    }
    setBusy(true)
    setError('')
    try {
      const created = await usersApi.create({ ...form, fullName: form.fullName.trim(), email: form.email.trim().toLowerCase() })
      setUsers((current) => [...current, created])
      setForm(null)
      setNotice('User created successfully.')
    } catch (err) { setError(err.message) }
    finally { setBusy(false) }
  }
  async function toggle(user) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const updated = await usersApi.setStatus(user.id, !user.isActive)
      setUsers((current) => current.map((item) => item.id === updated.id ? updated : item))
      setNotice(`${updated.fullName} ${updated.isActive ? 'activated' : 'deactivated'}.`)
    } catch (err) { setError(err.message) }
    finally { setBusy(false) }
  }

  return <div className="stack">
    <PageHeader title="Users & Roles" description="Manage team accounts and access to BuildWise."
      actions={<Button disabled={busy || loading} onClick={() => { setForm({ ...emptyForm }); setError(''); setNotice('') }}>+ Add User</Button>} />
    {error && <div role="alert">{error}{!form && <Button variant="secondary" onClick={load} disabled={busy}>Reload users</Button>}</div>}
    {notice && <p role="status">{notice}</p>}
    {form && <Card title="Create User">
      <form className="form-grid" onSubmit={create}>
        <TextInput label="Full Name" name="fullName" value={form.fullName} onChange={update('fullName')} required maxLength={150} />
        <TextInput label="Email" name="email" type="email" value={form.email} onChange={update('email')} required maxLength={255} />
        <TextInput label="Temporary Password" name="password" type="password" autoComplete="new-password" value={form.password} onChange={update('password')} required minLength={8} hint="At least 8 characters." />
        <SelectInput label="Role" name="roleName" value={form.roleName} onChange={update('roleName')} options={roles} />
        <div className="form-actions form-span">
          <Button type="button" variant="secondary" disabled={busy} onClick={() => { setForm(null); setError('') }}>Cancel</Button>
          <Button type="submit" disabled={busy}>{busy ? 'Creating…' : 'Create User'}</Button>
        </div>
      </form>
    </Card>}
    <Card>
      {loading ? <p role="status">Loading users…</p> : users.length === 0 ? <p>No users to display.</p> :
        <div className="table-wrap"><table className="data-table">
          <thead><tr>{['Name', 'Email', 'Role', 'Status', 'Created', 'Actions'].map((label) => <th key={label} scope="col">{label}</th>)}</tr></thead>
          <tbody>{users.map((user) => <tr key={user.id}>
            <td><strong>{user.fullName}</strong></td><td>{user.email}</td>
            <td>{user.roles.map((role) => roles.find((item) => item.value === role)?.label || role).join(', ')}</td>
            <td><StatusBadge status={user.isActive ? 'success' : 'neutral'}>{user.isActive ? 'Active' : 'Inactive'}</StatusBadge></td>
            <td>{new Date(user.createdAt).toLocaleDateString()}</td>
            <td><Button variant="secondary" disabled={busy || user.id === actor?.id} onClick={() => toggle(user)}>{user.isActive ? 'Deactivate' : 'Activate'}</Button></td>
          </tr>)}</tbody>
        </table></div>}
    </Card>
  </div>
}

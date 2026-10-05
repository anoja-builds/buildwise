import { useEffect, useMemo, useState } from 'react'
import { Button, Card, Drawer, EmptyState, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge, TextInput } from '../components/shared'
import { administrationApi } from '../services/administrationApi'
import './common/common.css'

const AVAILABLE_ROLES = [
  { value: 'SiteEngineer', label: 'Site Engineer (Field Material Requests)' },
  { value: 'SiteOfficer', label: 'Site Officer (Deliveries & Receiving)' },
  { value: 'SiteManager', label: 'Site Manager (Material Approvals)' },
  { value: 'ProcurementOfficer', label: 'Procurement Officer (RFQs & Quotations)' },
  { value: 'ProcurementManager', label: 'Procurement Manager (AI Recommendations & POs)' },
  { value: 'QualityInspector', label: 'Quality Inspector (Inspections & NCRs)' },
  { value: 'Administrator', label: 'Administrator (System & User Management)' },
  { value: 'Supplier', label: 'Supplier (External Quotation Portal)' },
]

const INITIAL_USER_FORM = {
  fullName: '',
  email: '',
  role: 'SiteEngineer',
  password: 'Passw0rd!'
}

export default function AdministrationPage() {
  const [users, setUsers] = useState([])
  const [audit, setAudit] = useState([])
  const [health, setHealth] = useState(null)
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  // Add User Drawer state
  const [isAddUserOpen, setIsAddUserOpen] = useState(false)
  const [userForm, setUserForm] = useState(INITIAL_USER_FORM)
  const [creatingUser, setCreatingUser] = useState(false)
  const [formError, setFormError] = useState('')

  async function load() {
    setLoading(true); setError('')
    try {
      const [nextUsers, nextAudit, nextHealth] = await Promise.all([
        administrationApi.listUsers(search || undefined),
        administrationApi.auditLogs(),
        administrationApi.health()
      ])
      setUsers(nextUsers); setAudit(nextAudit); setHealth(nextHealth)
    } catch (err) { setError(err.message) } finally { setLoading(false) }
  }
  useEffect(() => { load() }, [])

  const roles = useMemo(() => {
    const list = [...new Set([...AVAILABLE_ROLES.map(r => r.value), ...users.flatMap((user) => user.roles)])]
    return list.sort()
  }, [users])

  async function toggleUser(user) {
    try {
      await administrationApi.setUserActive(user.id, !user.isActive)
      setMessage(`${user.fullName} is now ${user.isActive ? 'inactive' : 'active'}.`)
      await load()
    } catch (err) { setError(err.message) }
  }

  async function changeRole(user, event) {
    const next = [event.target.value]
    try {
      await administrationApi.setUserRoles(user.id, next)
      setMessage(`Role updated for ${user.fullName} to ${event.target.value}.`)
      await load()
    } catch (err) { setError(err.message) }
  }

  async function handleCreateUser(e) {
    e.preventDefault()
    setFormError('')
    if (!userForm.fullName.trim() || !userForm.email.trim() || !userForm.password.trim()) {
      setFormError('Please fill in all required fields.')
      return
    }
    setCreatingUser(true)
    try {
      const created = await administrationApi.createUser(userForm)
      setMessage(`User "${created.fullName}" created successfully! A welcome email with login credentials has been sent to ${created.email}.`)
      setUserForm(INITIAL_USER_FORM)
      setIsAddUserOpen(false)
      await load()
    } catch (err) {
      setFormError(err.message)
    } finally {
      setCreatingUser(false)
    }
  }

  function generateRandomPassword() {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$'
    let pass = 'BW-'
    for (let i = 0; i < 8; i++) pass += chars.charAt(Math.floor(Math.random() * chars.length))
    setUserForm(prev => ({ ...prev, password: pass }))
  }

  if (loading) return <LoadingState message="Loading administration workspace…" />
  if (error) return <ErrorState message={error} onRetry={load} />

  return (
    <div className="stack">
      <PageHeader
        eyebrow="BuildWise"
        title="Administration"
        description="Protected user, role, audit, and system-health workspace."
        actions={<Button onClick={() => { setIsAddUserOpen(true); setFormError('') }}>+ Add New User</Button>}
      />

      {message && (
        <Card style={{ backgroundColor: '#f0fdf4', borderColor: '#bbf7d0', color: '#166534' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span>✓ <strong>{message}</strong></span>
            <Button variant="secondary" onClick={() => setMessage('')} style={{ fontSize: '0.8rem', padding: '0.2rem 0.6rem' }}>Dismiss</Button>
          </div>
        </Card>
      )}

      <div className="form-grid">
        <Card title="System health" subtitle="Database and internal agent reachability">
          <div className="stack">
            <div>API status: <StatusBadge status={health?.status === 'healthy' ? 'success' : 'warning'}>{health?.status ?? 'unknown'}</StatusBadge></div>
            <div>PostgreSQL: <StatusBadge status={health?.database ? 'success' : 'danger'}>{health?.database ? 'Connected' : 'Unavailable'}</StatusBadge></div>
            {Object.entries(health?.services ?? {}).map(([name, up]) => (
              <div key={name}>{name}: <StatusBadge status={up ? 'success' : 'danger'}>{up ? 'UP' : 'DOWN'}</StatusBadge></div>
            ))}
          </div>
        </Card>

        <Card title="User directory" subtitle={`${users.length} application users registered`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem', flexWrap: 'wrap', gap: '0.5rem' }}>
            <form onSubmit={(e) => { e.preventDefault(); load() }} style={{ display: 'flex', gap: '0.5rem', flex: 1 }}>
              <TextInput
                id="user-search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search by name or email…"
              />
              <Button type="submit" variant="secondary">Search</Button>
            </form>
            <Button onClick={() => { setIsAddUserOpen(true); setFormError('') }}>+ Add User</Button>
          </div>

          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Assigned Role</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <strong>{user.fullName}</strong>
                      <br />
                      <small className="muted">{user.email}</small>
                    </td>
                    <td>
                      <SelectInput
                        id={`role-${user.id}`}
                        value={user.roles[0] ?? 'SiteEngineer'}
                        onChange={(e) => changeRole(user, e)}
                        options={AVAILABLE_ROLES.map((role) => ({ value: role.value, label: role.value }))}
                      />
                    </td>
                    <td>
                      <StatusBadge status={user.isActive ? 'success' : 'danger'}>
                        {user.isActive ? 'Active' : 'Inactive'}
                      </StatusBadge>
                    </td>
                    <td>
                      <Button variant="secondary" onClick={() => toggleUser(user)}>
                        {user.isActive ? 'Deactivate' : 'Activate'}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      </div>

      <Card title="Recent audit events" subtitle="Authenticated state-changing API requests, without request bodies or secrets">
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Time</th>
                <th>User</th>
                <th>Action</th>
                <th>Method / Path</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {audit.length === 0 ? (
                <tr>
                  <td colSpan="5">
                    <EmptyState title="No audit events" message="Mutations will appear here after users perform actions." />
                  </td>
                </tr>
              ) : (
                audit.map((event) => (
                  <tr key={event.id}>
                    <td>{new Date(event.createdAt).toLocaleString()}</td>
                    <td>{event.userId ? `User #${event.userId}` : 'System'}</td>
                    <td><strong>{event.action}</strong></td>
                    <td><code>{event.httpMethod} {event.requestPath}</code></td>
                    <td>
                      <StatusBadge status={event.statusCode < 400 ? 'success' : 'danger'}>
                        {event.statusCode}
                      </StatusBadge>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Add User Drawer */}
      <Drawer
        open={isAddUserOpen}
        title="Provision New User"
        subtitle="Create an internal staff or supplier account and dispatch welcome login credentials via email."
        onClose={() => setIsAddUserOpen(false)}
      >
        <form onSubmit={handleCreateUser} className="stack" style={{ gap: '1.25rem' }}>
          {formError && <div className="field__error" role="alert">{formError}</div>}

          <div className="detail-row" style={{ backgroundColor: '#f8fafc', padding: '0.75rem', borderRadius: '8px', border: '1px solid #e2e8f0' }}>
            <span>📧 <strong>Email Notification:</strong></span>
            <small>Upon creation, an automated welcome email containing their login URL, username, and temporary password will be sent to the user's email address.</small>
          </div>

          <TextInput
            id="new-user-fullname"
            label="Full Name *"
            required
            value={userForm.fullName}
            onChange={(e) => setUserForm(prev => ({ ...prev, fullName: e.target.value }))}
            placeholder="e.g. Priyantha Silva"
          />

          <TextInput
            id="new-user-email"
            label="Email Address *"
            type="email"
            required
            value={userForm.email}
            onChange={(e) => setUserForm(prev => ({ ...prev, email: e.target.value }))}
            placeholder="e.g. priyantha.silva@buildwise.demo"
          />

          <SelectInput
            id="new-user-role"
            label="Assigned System Role *"
            value={userForm.role}
            onChange={(e) => setUserForm(prev => ({ ...prev, role: e.target.value }))}
            options={AVAILABLE_ROLES}
          />

          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.25rem' }}>
              <label htmlFor="new-user-password" style={{ fontWeight: 600, fontSize: '0.875rem' }}>Temporary Password *</label>
              <button
                type="button"
                onClick={generateRandomPassword}
                style={{ background: 'none', border: 'none', color: '#2563eb', cursor: 'pointer', fontSize: '0.8rem', textDecoration: 'underline' }}
              >
                Generate random
              </button>
            </div>
            <TextInput
              id="new-user-password"
              type="text"
              required
              value={userForm.password}
              onChange={(e) => setUserForm(prev => ({ ...prev, password: e.target.value }))}
              placeholder="Minimum 6 characters"
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '1rem' }}>
            <Button variant="secondary" onClick={() => setIsAddUserOpen(false)} disabled={creatingUser}>
              Cancel
            </Button>
            <Button type="submit" disabled={creatingUser}>
              {creatingUser ? 'Provisioning User…' : 'Create User & Send Email'}
            </Button>
          </div>
        </form>
      </Drawer>
    </div>
  )
}


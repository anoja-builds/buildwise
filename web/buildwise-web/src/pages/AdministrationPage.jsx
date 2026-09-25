import { useEffect, useMemo, useState } from 'react'
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge, TextInput } from '../components/shared'
import { administrationApi } from '../services/administrationApi'
import './common/common.css'

export default function AdministrationPage() {
  const [users, setUsers] = useState([])
  const [audit, setAudit] = useState([])
  const [health, setHealth] = useState(null)
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  async function load() {
    setLoading(true); setError('')
    try {
      const [nextUsers, nextAudit, nextHealth] = await Promise.all([administrationApi.listUsers(search || undefined), administrationApi.auditLogs(), administrationApi.health()])
      setUsers(nextUsers); setAudit(nextAudit); setHealth(nextHealth)
    } catch (err) { setError(err.message) } finally { setLoading(false) }
  }
  useEffect(() => { load() }, [])

  const roles = useMemo(() => [...new Set(users.flatMap((user) => user.roles))].sort(), [users])
  async function toggleUser(user) {
    try { await administrationApi.setUserActive(user.id, !user.isActive); setMessage(`${user.fullName} is now ${user.isActive ? 'inactive' : 'active'}.`); await load() } catch (err) { setError(err.message) }
  }
  async function changeRole(user, event) {
    const next = [...new Set([...user.roles.filter((role) => role !== event.target.value), event.target.value])]
    try { await administrationApi.setUserRoles(user.id, next); setMessage(`Roles updated for ${user.fullName}.`); await load() } catch (err) { setError(err.message) }
  }

  if (loading) return <LoadingState message="Loading administration workspace…" />
  if (error) return <ErrorState message={error} onRetry={load} />
  return <div className="stack">
    <PageHeader eyebrow="BuildSupply LK" title="Administration" description="Protected user, role, audit, and system-health workspace." />
    {message && <Card><strong>{message}</strong></Card>}
    <div className="form-grid">
      <Card title="System health" subtitle="Database and internal agent reachability">
        <div className="stack"><div>API status: <StatusBadge tone={health?.status === 'healthy' ? 'success' : 'warning'}>{health?.status ?? 'unknown'}</StatusBadge></div><div>PostgreSQL: <StatusBadge tone={health?.database ? 'success' : 'danger'}>{health?.database ? 'Connected' : 'Unavailable'}</StatusBadge></div>{Object.entries(health?.services ?? {}).map(([name, up]) => <div key={name}>{name}: <StatusBadge tone={up ? 'success' : 'danger'}>{up ? 'UP' : 'DOWN'}</StatusBadge></div>)}</div>
      </Card>
      <Card title="User directory" subtitle={`${users.length} application users`}>
        <form onSubmit={(e) => { e.preventDefault(); load() }}><div className="toolbar__filters"><TextInput label="Search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name or email" /><Button type="submit">Search</Button></div></form>
        <div className="table-wrap"><table className="data-table"><thead><tr><th>User</th><th>Roles</th><th>Status</th><th>Action</th></tr></thead><tbody>{users.map((user) => <tr key={user.id}><td><strong>{user.fullName}</strong><br/><small>{user.email}</small></td><td><SelectInput label="Change role" value={user.roles[0] ?? ''} onChange={(e) => changeRole(user, e)} options={roles.map((role) => ({ value: role, label: role }))} /></td><td><StatusBadge tone={user.isActive ? 'success' : 'danger'}>{user.isActive ? 'Active' : 'Inactive'}</StatusBadge></td><td><Button variant="secondary" onClick={() => toggleUser(user)}>{user.isActive ? 'Deactivate' : 'Activate'}</Button></td></tr>)}</tbody></table></div>
      </Card>
    </div>
    <Card title="Recent audit events" subtitle="Authenticated state-changing API requests, without request bodies or secrets"><div className="table-wrap"><table className="data-table"><thead><tr><th>Time</th><th>User</th><th>Action</th><th>Status</th></tr></thead><tbody>{audit.length === 0 ? <tr><td colSpan="4"><EmptyState title="No audit events" message="Mutations will appear here after users perform actions." /></td></tr> : audit.map((event) => <tr key={event.id}><td>{new Date(event.createdAt).toLocaleString()}</td><td>{event.userId ?? 'System'}</td><td>{event.action}</td><td><StatusBadge tone={event.statusCode < 400 ? 'success' : 'danger'}>{event.statusCode}</StatusBadge></td></tr>)}</tbody></table></div></Card>
  </div>
}

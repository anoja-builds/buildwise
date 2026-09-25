import { authApi } from './authApi'

const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api'

async function request(path, { method = 'GET', body } = {}) {
  const session = authApi.loadSession()
  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  })
  const data = await response.json().catch(() => ({}))
  if (!response.ok) throw new Error(data?.message || data?.error || `Request failed (${response.status})`)
  return data
}

export const administrationApi = {
  listUsers: (search) => request(`/admin/users${search ? `?search=${encodeURIComponent(search)}` : ''}`),
  setUserActive: (id, isActive) => request(`/admin/users/${id}/active`, { method: 'PATCH', body: { isActive } }),
  setUserRoles: (id, roles) => request(`/admin/users/${id}/roles`, { method: 'PUT', body: { roles } }),
  auditLogs: () => request('/admin/audit-logs?pageSize=25'),
  health: () => request('/admin/health'),
}

export default administrationApi

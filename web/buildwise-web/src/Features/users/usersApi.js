import { authApi } from '../../services/authApi'

const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api').replace(/\/+$/, '')

async function request(path = '', method = 'GET', body) {
  const session = authApi.loadSession()
  const response = await fetch(`${API_BASE}/users${path}`, {
    method,
    headers: {
      ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (response.status === 401) {
    authApi.clearSession()
    window.dispatchEvent(new CustomEvent('buildwise:unauthorized'))
    throw new Error('Your session has expired. Please sign in again.')
  }
  const data = await response.json().catch(() => null)
  if (!response.ok) {
    const validation = data?.errors ? Object.values(data.errors).flat().join(' ') : ''
    throw new Error(data?.error || validation || data?.detail || data?.title || `Request failed (${response.status}).`)
  }
  return data
}

export const usersApi = {
  list: () => request(),
  create: (body) => request('', 'POST', body),
  setStatus: (id, isActive) => request(`/${id}/status`, 'PATCH', { isActive }),
}

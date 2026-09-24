import { authApi } from '../../../services/authApi'

const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api').replace(/\/+$/, '')

async function request(path, { method = 'GET', body } = {}) {
  const session = authApi.loadSession()
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), 120000)
  try {
    const response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: {
        ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}),
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    })
    if (response.status === 401) {
      authApi.clearSession()
      window.dispatchEvent(new CustomEvent('buildwise:unauthorized'))
      throw new Error('Your session has expired. Please sign in again.')
    }
    const data = response.status === 204 ? null : await response.json().catch(() => null)
    if (!response.ok) {
      const validation = data?.errors ? Object.values(data.errors).flat().join(' ') : ''
      const error = new Error(response.status === 403
        ? 'You are not authorized to access quality management.'
        : data?.detail || validation || data?.error || data?.title || `Request failed (${response.status}).`)
      error.status = response.status
      error.data = data
      throw error
    }
    return data
  } catch (error) {
    if (error.name === 'AbortError') throw new Error('The request timed out. Refresh the record before retrying an action; it may have reached the server.')
    if (error instanceof TypeError) throw new Error('Unable to reach the API. Check your connection. Refresh before retrying an action; it may have reached the server.')
    throw error
  } finally {
    clearTimeout(timeout)
  }
}

export const qualityApi = {
  listInspections: () => request('/inspections'),
  getInspection: (id) => request(`/inspections/${id}`),
  listNcrs: () => request('/non-conformances'),
  getNcr: (id) => request(`/non-conformances/${id}`),
  createNcr: (body) => request('/non-conformances', { method: 'POST', body }),
  updateCorrectiveAction: (id, correctiveAction) => request(`/non-conformances/${id}/corrective-action`, { method: 'PATCH', body: { correctiveAction } }),
  resolveNcr: (id) => request(`/non-conformances/${id}/resolve`, { method: 'POST' }),
  closeNcr: (id) => request(`/non-conformances/${id}/close`, { method: 'POST' }),
  analyseInspection: (id) => request(`/quality-risk-agent/inspections/${id}/analyse`, { method: 'POST' }),
  getWorkflow: (id) => request(`/quality-risk-agent/workflows/${id}`),
}

import { authApi } from './authApi'

const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api'

// Shared auth-aware request helper used by Component 1 + 4 pages.
// Reuses authApi's session storage so the signed-in user's bearer token
// is attached exactly like the login/procurement calls.
async function request(path, { method = 'GET', body } = {}) {
  const session = authApi.loadSession()
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    throw new Error(data?.message || `Request failed (${res.status})`)
  }
  return data
}

// Static reference data for the "Create Request" form dropdown, matching the
// seeded Project #1 "Riverside Apartments — Block C" and the OPC Cement material
// used across the spec scenarios (§4 / §11). There are no /api/projects or
// /api/materials list endpoints yet, so the form uses these known seed values.
const MOCK_PROJECTS = [
  { id: 1, name: 'Riverside Apartments — Block C' },
]

const MOCK_MATERIALS = [
  { id: 1, name: 'OPC Cement', unit: 'bags' },
]

export const qualityApi = {
  // Material Requests (Component 1)
  listMyMaterialRequests: () => request('/material-requests/my'),
  listMaterialRequests: () => request('/material-requests?status=PendingApproval'),
  getMaterialRequest: (id) => request(`/material-requests/${id}`),
  createMaterialRequest: (payload) => request('/material-requests', { method: 'POST', body: payload }),
    getMaterialRequestHistory: (id) => request(`/material-requests/${id}/history`),
  reviseMaterialRequest: (id, payload) => request(`/material-requests/${id}/revise`, { method: 'POST', body: payload }),

  listInspections: (params = {}) => {
    const query = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => { if (value !== undefined && value !== null && value !== '') query.set(key, value) })
    return request(`/quality-inspections${query.size ? `?${query}` : ''}`)
  },
  getInspection: (id) => request(`/quality-inspections/${id}`),
  listNonConformances: () => request('/quality-inspections/non-conformances'),
  transitionNonConformance: (id, payload) => request(`/quality-inspections/non-conformances/${id}/transition`, { method: 'POST', body: payload }),
  listNotifications: (unreadOnly = false) => request(`/notifications?unreadOnly=${unreadOnly}`),
  markNotificationRead: (id) => request(`/notifications/${id}/read`, { method: 'PUT' }),

  // Deliveries (Component 3)
  listDeliveries: () => request('/deliveries'),
  listConfirmedPurchaseOrders: () => request('/deliveries/confirmed-orders'),
  recordDelivery: (payload) => request('/deliveries', { method: 'POST', body: payload }),

  // Agent observability (read-only)
  listAgentWorkflows: (params = {}) => {
    const query = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') query.set(key, value)
    })
    return request(`/agent-workflows${query.size ? `?${query}` : ''}`)
  },
  getAgentWorkflow: (id) => request(`/agent-workflows/${id}`),

  // Reference data endpoints
  listProjects: () => request('/projects').catch(() => MOCK_PROJECTS),
  listMaterials: () => request('/materials').catch(() => MOCK_MATERIALS),

  // Dropdown seeds fallback
  projects: () => MOCK_PROJECTS,
  materials: () => MOCK_MATERIALS,
}

export default qualityApi

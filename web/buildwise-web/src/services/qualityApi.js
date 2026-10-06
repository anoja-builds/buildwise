import { authApi } from './authApi'
import { API_BASE, fetchOrThrow } from './apiTransport'

// Shared auth-aware request helper used by Component 1 + 4 pages.
// Reuses authApi's session storage so the signed-in user's bearer token
// is attached exactly like the login/procurement calls.
async function request(path, { method = 'GET', body } = {}) {
  const session = authApi.loadSession()
  const res = await fetchOrThrow(`${API_BASE}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    // `status` and `payload` travel with the error so a form can map RFC 7807
    // ValidationProblemDetails onto its fields instead of showing one banner.
    const error = new Error(data?.message || `Request failed (${res.status})`)
    error.status = res.status
    error.payload = data
    throw error
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
  // `status` is a MaterialRequestStatus ("PendingApproval", "Approved", …) or
  // 'all' for every status. Omit it to keep the pending queue (the default).
  // An empty/blank value must never reach the query string: `/material-requests`
  // with `?status=` relied on the API tolerating a blank filter, and any change
  // there would silently hide new rows from the procurement desk.
  listMaterialRequests: (status = 'PendingApproval') => {
    const effective = typeof status === 'string' ? status.trim() : ''
    if (!effective) return request('/material-requests?status=all')
    return request(`/material-requests?status=${encodeURIComponent(effective)}`)
  },
  getMaterialRequest: (id) => request(`/material-requests/${id}`),
  createMaterialRequest: (payload) => request('/material-requests', { method: 'POST', body: payload }),
  // STEP 2 approval decision (Procurement/Site Manager + Admin only):
  // decision is 'Approved' | 'Rejected' | 'RevisionRequested'; a comment is
  // mandatory for Rejected (enforced server-side by MaterialRequestService).
  decideMaterialRequest: (id, decision, comments) =>
    request(`/material-requests/${id}/approval`, { method: 'POST', body: { decision, comments: comments || null } }),
  getMaterialRequestHistory: (id) => request(`/material-requests/${id}/history`),
  // STEP 3: trigger the RequestAnalysisAgent for one material request.
  // Manager/Site Manager/Admin only (MaterialRequestApprovalOnly). Returns
  // { requestId, flags, status }, e.g. { flags: ['HIGH_URGENCY',
  // 'LARGE_QUANTITY_ORDER'], status: 'Analyzed' }. The result is advisory — the
  // agent never approves anything on its own.
  analyzeRequest: (id) => request(`/agent/analyze-request/${id}`, { method: 'POST' }),
  reviseMaterialRequest: (id, payload) => request(`/material-requests/${id}/revise`, { method: 'POST', body: payload }),

  listInspections: (params = {}) => {
    const query = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => { if (value !== undefined && value !== null && value !== '') query.set(key, value) })
    return request(`/quality-inspections${query.size ? `?${query}` : ''}`)
  },
  getInspection: (id) => request(`/quality-inspections/${id}`),
  completeInspection: (payload) => request('/quality-inspections', { method: 'POST', body: payload }),
  // COMPONENT 4 agent: runs the QualityRiskAnalysisAgent (:8004) over one
  // completed inspection. Advisory only — it does not create the NCR or change
  // the inspection status; those are decided by the backend and the inspector.
  analyzeQualityRisk: (id) => request(`/quality-inspections/${id}/risk-analysis`, { method: 'POST' }),
  listNonConformances: () => request('/quality-inspections/non-conformances'),
  transitionNonConformance: (id, payload) => request(`/quality-inspections/non-conformances/${id}/transition`, { method: 'POST', body: payload }),
  listNotifications: (unreadOnly = false) => request(`/notifications?unreadOnly=${unreadOnly}`),
  markNotificationRead: (id) => request(`/notifications/${id}/read`, { method: 'PUT' }),

  dashboard: () => request('/dashboard'),

  // Deliveries (Component 3)
  listDeliveries: () => request('/deliveries'),
  listConfirmedPurchaseOrders: () => request('/deliveries/confirmed-orders'),
  recordDelivery: (payload) => request('/deliveries', { method: 'POST', body: payload }),
  // COMPONENT 3 agent: runs the DeliveryDiscrepancyAgent (:8003) over one
  // recorded delivery. Advisory only — it does not change the delivery status.
  analyzeDeliveryDiscrepancy: (id) => request(`/deliveries/${id}/discrepancy-analysis`, { method: 'POST' }),

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

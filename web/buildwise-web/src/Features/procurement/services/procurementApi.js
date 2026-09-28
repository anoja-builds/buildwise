import { authApi } from '../../../services/authApi'

const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api').replace(/\/+$/, '')

async function request(path, { method = 'GET', body } = {}) {
  const session = authApi.loadSession()
  const headers = { ...(body ? { 'Content-Type': 'application/json' } : {}), ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}) }
  let res
  try {
    res = await fetch(`${API_BASE}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined })
  } catch {
    throw new Error('Cannot reach BuildWise API. Check the connection and retry.')
  }
  if (res.status === 401) {
    authApi.clearSession()
    window.dispatchEvent(new CustomEvent('buildwise:unauthorized'))
    throw new Error('Your session has expired. Please sign in again.')
  }
  if (!res.ok) {
    const text = await res.text()
    let message = text || `Request failed (${res.status})`
    try { const data = JSON.parse(text); message = data.error || data.title || message } catch { /* Plain text API errors are supported. */ }
    throw new Error(message)
  }
  return res.status === 204 ? null : res.json()
}
const qs = (params = {}) => {
  const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '')
  return entries.length ? `?${new URLSearchParams(entries)}` : ''
}
const requestDetail = data => ({ ...data, items: data.items.map(i => ({ ...i, unit: i.materialUnit, requestedQuantity: i.quantity })) })
export const procurementApi = {
  listSuppliers: params => request(`/suppliers${qs(params)}`),
  getSupplier: id => request(`/suppliers/${id}`),
  createSupplier: body => request('/suppliers', { method: 'POST', body }),
  updateSupplier: (id, body) => request(`/suppliers/${id}`, { method: 'PUT', body }),
  updateSupplierStatus: (id, status) => request(`/suppliers/${id}/status`, { method: 'PATCH', body: { status } }),
  listApprovedMaterialRequests: () => request('/MaterialRequests?status=Approved'),
  getMaterialRequest: id => request(`/MaterialRequests/${id}`).then(requestDetail),
  listQuotationsForRequest: id => request(`/material-requests/${id}/quotations`),
  getQuotation: id => request(`/quotations/${id}`),
  createQuotation: (id, body) => request(`/material-requests/${id}/quotations`, { method: 'POST', body }),
  deleteQuotation: id => request(`/quotations/${id}`, { method: 'DELETE' }),
  compareQuotations: id => request(`/material-requests/${id}/quotations/compare`),
  startWorkflow: (id, body = {}) => request(`/material-requests/${id}/procurement-workflow`, { method: 'POST', body }),
  getLatestWorkflow: id => request(`/material-requests/${id}/procurement-workflow`),
  getWorkflow: id => request(`/procurement-workflow/${id}`),
  getWorkflowHistory: id => request(`/procurement-workflow/${id}/history`),
  recordDecision: (id, decision, comment) => request(`/procurement-workflow/${id}/decision`, { method: 'POST', body: { decision, comment } }),
  createPurchaseOrderFromWorkflow: id => request(`/procurement-workflow/${id}/purchase-order`, { method: 'POST' }),
  listPurchaseOrders: params => request(`/purchase-orders${qs(params)}`),
  getPurchaseOrder: id => request(`/purchase-orders/${id}`),
  updatePurchaseOrderStatus: (id, status) => request(`/purchase-orders/${id}/status`, { method: 'PATCH', body: { status } })
}
export default procurementApi

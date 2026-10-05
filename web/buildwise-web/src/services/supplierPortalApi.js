import { fetchOrThrow, API_BASE } from './apiTransport'

const STORAGE_KEY = 'buildwise.auth'

/**
 * Client for the external Supplier portal (`/api/supplier-portal/*`).
 *
 * Every endpoint here is scoped server-side to the supplier bound to the
 * caller's JWT, so this client never sends a supplier id — there is nothing for
 * a caller to tamper with. Keep it that way: adding a `supplierId` parameter
 * here would be a red flag, not a feature.
 */
async function request(path, options = {}) {
  const session = (() => {
    try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null') } catch { return null }
  })()

  const res = await fetchOrThrow(`${API_BASE}${path}`, {
    method: options.method || 'GET',
    headers: {
      'Content-Type': 'application/json',
      ...(session?.token ? { Authorization: `Bearer ${session.token}` } : {}),
    },
    body: options.body ? JSON.stringify(options.body) : undefined,
  })

  if (res.status === 204) return null

  if (!res.ok) {
    let message = `Request failed (${res.status})`
    try {
      const data = await res.json()
      message = data?.message || data?.error || data?.title || (typeof data === 'string' ? data : message)
    } catch {
      // no JSON body
    }
    throw new Error(message)
  }

  return res.json()
}

export const supplierPortalApi = {
  getProfile: () => request('/supplier-portal/profile'),
  listRfqs: () => request('/supplier-portal/rfqs'),
  getRfqItems: (rfqId) => request(`/supplier-portal/rfqs/${rfqId}/items`),
  listQuotations: () => request('/supplier-portal/quotations'),
  submitQuotation: (rfqId, payload) =>
    request(`/supplier-portal/rfqs/${rfqId}/quotations`, { method: 'POST', body: payload }),
  listPurchaseOrders: () => request('/supplier-portal/purchase-orders'),
}

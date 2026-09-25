import { authApi } from '../../../services/authApi'

const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api'

const todayPlus = (days) => {
  const d = new Date()
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

// Mirrors the spec's cement scenario (§4/§11) so the UI is demonstrable
// even when BuildWise.Api / the agent service are not running locally.
const MOCK = {
  suppliers: [
    { id: 1, name: 'Supplier A Building Materials', contactPerson: 'Nimal Perera', email: 'sales@suppliera.lk', phone: '+94 11 234 5678', address: '12 Galle Road, Colombo', status: 'Active', createdAt: '2026-06-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z' },
    { id: 2, name: 'Supplier B Traders', contactPerson: 'Kamal Silva', email: 'info@supplierb.lk', phone: '+94 11 345 6789', address: '45 Kandy Road, Kandy', status: 'Suspended', createdAt: '2026-05-12T00:00:00Z', updatedAt: '2026-08-02T00:00:00Z' },
    { id: 3, name: 'Supplier C Wholesale', contactPerson: 'Anusha Fernando', email: 'quotes@supplierc.lk', phone: '+94 11 456 7890', address: '8 Negombo Road, Gampaha', status: 'Active', createdAt: '2026-04-20T00:00:00Z', updatedAt: '2026-04-20T00:00:00Z' }
  ],
  materialRequests: [
    { id: 101, projectId: 1, projectName: 'Riverside Apartments — Block C', requiredDate: todayPlus(10), reason: 'Foundation pour for Block C', status: 'Approved', itemCount: 1, quotationCount: 3 }
  ],
  materialRequestDetail: {
    id: 101,
    projectId: 1,
    projectName: 'Riverside Apartments — Block C',
    requiredDate: todayPlus(10),
    reason: 'Foundation pour for Block C',
    status: 'Approved',
    items: [{ id: 1001, materialId: 5, materialName: 'Cement (50kg bag)', unit: 'bag', requestedQuantity: 250, notes: null }]
  },
  quotations: [
    { id: 501, materialRequestId: 101, supplierId: 1, supplierName: 'Supplier A Building Materials', supplierStatus: 'Active', quotationDate: todayPlus(-2), validUntil: todayPlus(20), status: 'UnderReview', totalAmount: 525000, createdAt: '2026-09-01T00:00:00Z', items: [{ id: 1, materialRequestItemId: 1001, materialName: 'Cement (50kg bag)', unit: 'bag', quantity: 250, unitPrice: 2100, lineTotal: 525000 }] },
    { id: 502, materialRequestId: 101, supplierId: 2, supplierName: 'Supplier B Traders', supplierStatus: 'Suspended', quotationDate: todayPlus(-2), validUntil: todayPlus(15), status: 'UnderReview', totalAmount: 510000, createdAt: '2026-09-01T00:00:00Z', items: [{ id: 2, materialRequestItemId: 1001, materialName: 'Cement (50kg bag)', unit: 'bag', quantity: 250, unitPrice: 2040, lineTotal: 510000 }] },
    { id: 503, materialRequestId: 101, supplierId: 3, supplierName: 'Supplier C Wholesale', supplierStatus: 'Active', quotationDate: todayPlus(-1), validUntil: todayPlus(18), status: 'UnderReview', totalAmount: 410000, createdAt: '2026-09-02T00:00:00Z', items: [{ id: 3, materialRequestItemId: 1001, materialName: 'Cement (50kg bag)', unit: 'bag', quantity: 200, unitPrice: 2050, lineTotal: 410000 }] }
  ],
  workflow: {
    id: 9001,
    materialRequestId: 101,
    objective: 'Recommend the best eligible quotation for cement (250 bags).',
    status: 'AwaitingApproval',
    approvalStatus: 'Pending',
    finalOutcome: null,
    recommendation: {
      recommendedQuotationId: 501,
      recommendedSupplierId: 1,
      recommendedSupplierName: 'Supplier A Building Materials',
      rationale: 'Supplier A is Active, fully covers the requested 250 bags, and its total (525,000) is the lowest among fully-covering, eligible offers.',
      rankedAlternatives: [
        { quotationId: 501, supplierId: 1, supplierName: 'Supplier A Building Materials', rank: 1, totalAmount: 525000, reason: 'Active supplier, full coverage (250/250), lowest eligible total.' },
        { quotationId: 503, supplierId: 3, supplierName: 'Supplier C Wholesale', rank: 2, totalAmount: 410000, reason: 'Cheapest overall, but covers only 200/250 bags — partial coverage, not eligible as sole winner.' }
      ],
      warnings: ['Supplier B (quotation #502) is Suspended and was excluded from recommendation.', 'Supplier C (quotation #503) covers only 200/250 units — flagged as partial coverage.']
    },
    validation: { isValid: true, errors: [] },
    steps: [
      { id: 1, agentRole: 'QuotationSupplierAnalysisAgent', stepName: 'Filter eligible quotations', stepOrder: 1, status: 'Completed', structuredResult: null, validationResult: null, errorMessage: null, startedAt: '2026-09-02T09:00:00Z', completedAt: '2026-09-02T09:00:02Z' },
      { id: 2, agentRole: 'QuotationSupplierAnalysisAgent', stepName: 'Rank & recommend', stepOrder: 2, status: 'Completed', structuredResult: null, validationResult: null, errorMessage: null, startedAt: '2026-09-02T09:00:02Z', completedAt: '2026-09-02T09:00:04Z' },
      { id: 3, agentRole: 'QuotationSupplierAnalysisAgent', stepName: 'Deterministic validation', stepOrder: 3, status: 'Completed', structuredResult: null, validationResult: null, errorMessage: null, startedAt: '2026-09-02T09:00:04Z', completedAt: '2026-09-02T09:00:05Z' }
    ],
    createdAt: '2026-09-02T09:00:00Z',
    updatedAt: '2026-09-02T09:00:05Z'
  },
  purchaseOrders: [
    { id: 7001, quotationId: 501, materialRequestId: 101, supplierId: 1, supplierName: 'Supplier A Building Materials', orderDate: todayPlus(-1), expectedDeliveryDate: todayPlus(7), status: 'Created', totalAmount: 525000, createdAt: '2026-09-03T00:00:00Z', updatedAt: '2026-09-03T00:00:00Z', items: [{ id: 1, quotationItemId: 1, materialName: 'Cement (50kg bag)', unit: 'bag', orderedQuantity: 250, unitPrice: 2100, lineTotal: 525000 }] }
  ]
}

let usingMockFallback = false
export const isUsingMockData = () => usingMockFallback

async function request(path, { method = 'GET', body, mockFallback } = {}) {
  try {
    const session = authApi.loadSession()
    const headers = {}
    if (body) headers['Content-Type'] = 'application/json'
    if (session?.token) headers['Authorization'] = `Bearer ${session.token}`

    const res = await fetch(`${API_BASE}${path}`, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined
    })

        if (res.status === 401) {
      if (session?.token) {
        // Authenticated request whose token was rejected — the real session-expired flow.
        authApi.clearSession()
        window.dispatchEvent(new CustomEvent('buildwise:unauthorized'))
        throw new Error('Your session has expired. Please sign in again.')
      }
      // Unauthenticated caller (e.g. vitest, or a fresh demo tab) hitting an API
      // that requires auth. Treat this like a network failure so the mock
      // fallback engages instead of surfacing a 401 to the user.
      throw new TypeError('Failed to fetch')
    }

    if (!res.ok) {
      let message = `Request failed (${res.status})`
      try {
        const data = await res.json()
        message = data?.error || data?.title || (typeof data === 'string' ? data : message)
      } catch {
        // response had no JSON body
      }
      throw new Error(message)
    }

    usingMockFallback = false
    if (res.status === 204) return null
    return await res.json()
  } catch (err) {
    if (mockFallback !== undefined && (err instanceof TypeError || err.message === 'Failed to fetch')) {
      usingMockFallback = true
      return typeof mockFallback === 'function' ? mockFallback() : mockFallback
    }
    throw err
  }
}

const qs = (params = {}) => {
  const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '')
  return entries.length ? `?${new URLSearchParams(entries).toString()}` : ''
}

export const procurementApi = {
  // Suppliers
  listSuppliers: (params) => request(`/suppliers${qs(params)}`, { mockFallback: () => ({ items: MOCK.suppliers, total: MOCK.suppliers.length, page: 1, pageSize: MOCK.suppliers.length }) }),
  getSupplier: (id) => request(`/suppliers/${id}`, { mockFallback: () => ({ ...MOCK.suppliers.find((s) => s.id === Number(id)), quotationHistory: MOCK.quotations.filter((q) => q.supplierId === Number(id)).map((q) => ({ quotationId: q.id, materialRequestId: q.materialRequestId, quotationDate: q.quotationDate, validUntil: q.validUntil, status: q.status, totalAmount: q.totalAmount })) }) }),
  createSupplier: (data) => request('/suppliers', { method: 'POST', body: data }),
  updateSupplier: (id, data) => request(`/suppliers/${id}`, { method: 'PUT', body: data }),
  updateSupplierStatus: (id, status) => request(`/suppliers/${id}/status`, { method: 'PATCH', body: { status } }),

  // Material requests (read-only, consumed from Component 1)
  listApprovedMaterialRequests: () => request('/material-requests?status=Approved', { mockFallback: () => MOCK.materialRequests }),
  getMaterialRequest: (id) => request(`/material-requests/${id}`, { mockFallback: () => MOCK.materialRequestDetail }),

  // Quotations
  listQuotationsForRequest: (requestId) => request(`/material-requests/${requestId}/quotations`, { mockFallback: () => MOCK.quotations }),
  getQuotation: (id) => request(`/quotations/${id}`, { mockFallback: () => MOCK.quotations.find((q) => q.id === Number(id)) }),
  createQuotation: (requestId, data) => request(`/material-requests/${requestId}/quotations`, { method: 'POST', body: data }),
  deleteQuotation: (id) => request(`/quotations/${id}`, { method: 'DELETE' }),
  compareQuotations: (requestId) => request(`/material-requests/${requestId}/quotations/compare`, {
    mockFallback: () => ({
      materialRequestId: MOCK.materialRequestDetail.id,
      projectName: MOCK.materialRequestDetail.projectName,
      rows: MOCK.materialRequestDetail.items.map((item) => ({
        materialRequestItemId: item.id,
        materialName: item.materialName,
        unit: item.unit,
        requestedQuantity: item.requestedQuantity,
        offers: MOCK.quotations.map((q) => ({
          quotationId: q.id,
          supplierId: q.supplierId,
          supplierName: q.supplierName,
          supplierStatus: q.supplierStatus,
          quantityOffered: q.items[0].quantity,
          unitPrice: q.items[0].unitPrice,
          lineTotal: q.items[0].lineTotal,
          coversFullQuantity: q.items[0].quantity >= item.requestedQuantity
        }))
      })),
      quotations: MOCK.quotations
    })
  }),

  // RFQs
  listRfqs: (status) => request(`/rfqs${qs({ status })}`, { mockFallback: () => [] }),
  getRfq: (id) => request(`/rfqs/${id}`, { mockFallback: () => null }),
  createRfq: (data) => request('/rfqs', { method: 'POST', body: data }),
  addRfqSuppliers: (id, supplierIds) => request(`/rfqs/${id}/suppliers`, { method: 'POST', body: { supplierIds } }),
  closeRfq: (id, reason) => request(`/rfqs/${id}/close`, { method: 'POST', body: { reason } }),

  // Agentic AI workflow
  startWorkflow: (requestId, body) => request(`/material-requests/${requestId}/procurement-workflow`, { method: 'POST', body: body ?? {}, mockFallback: () => ({ workflowId: MOCK.workflow.id, status: MOCK.workflow.status, message: 'Quotation & Supplier Analysis Agent completed (demo data — agent service not reachable).' }) }),
  getWorkflow: (workflowId) => request(`/procurement-workflow/${workflowId}`, { mockFallback: () => MOCK.workflow }),
  getWorkflowHistory: (workflowId) => request(`/procurement-workflow/${workflowId}/history`, { mockFallback: () => MOCK.workflow.steps }),
    recordDecision: (workflowId, decision, comment) => request(`/procurement-workflow/${workflowId}/decision`, { method: 'POST', body: { decision, comment } }),
  analyzeRequest: (requestId) => request(`/agent/analyze-request/${requestId}`, { method: 'POST' }),

  // Purchase orders
  createPurchaseOrderFromWorkflow: (workflowId) => request(`/procurement-workflow/${workflowId}/purchase-order`, { method: 'POST' }),
  listPurchaseOrders: (params) => request(`/purchase-orders${qs(params)}`, { mockFallback: () => ({ items: MOCK.purchaseOrders, total: MOCK.purchaseOrders.length, page: 1, pageSize: MOCK.purchaseOrders.length }) }),
  getPurchaseOrder: (id) => request(`/purchase-orders/${id}`, { mockFallback: () => MOCK.purchaseOrders.find((p) => p.id === Number(id)) }),
  updatePurchaseOrderStatus: (id, status) => request(`/purchase-orders/${id}/status`, { method: 'PATCH', body: { status } })
}

export default procurementApi

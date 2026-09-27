import { authApi } from '../../../services/authApi';

const API_BASE_URL = (import.meta.env?.VITE_API_BASE_URL || 'http://localhost:5078/api').replace(/\/+$/, '');

function getHeaders(hasBody = false) {
  const session = authApi.loadSession();
  const headers = {};
  if (hasBody) headers['Content-Type'] = 'application/json';
  if (session?.token) headers['Authorization'] = `Bearer ${session.token}`;
  return headers;
}

function handleUnauthorized(res) {
  if (res.status === 401) {
    authApi.clearSession();
    window.dispatchEvent(new CustomEvent('buildwise:unauthorized'));
    throw new Error('Your session has expired. Please sign in again.');
  }
}

export const procurementService = {
  async getSuppliers(status = '') {
    let url = `${API_BASE_URL}/suppliers`;
    if (status) url += `?status=${status}`;
    const res = await fetch(url, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch suppliers');
    return await res.json();
  },

  async createSupplier(payload) {
    const res = await fetch(`${API_BASE_URL}/suppliers`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(payload)
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to create supplier');
    return await res.json();
  },

  async getRfqs() {
    const res = await fetch(`${API_BASE_URL}/procurement/rfqs`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch RFQs');
    return await res.json();
  },

  async createRfq(payload) {
    const res = await fetch(`${API_BASE_URL}/procurement/rfqs`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(payload)
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to create RFQ');
    return await res.json();
  },

  async submitQuotation(payload) {
    const res = await fetch(`${API_BASE_URL}/procurement/quotations`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(payload)
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to record supplier quotation');
    return await res.json();
  },

  async getQuotationComparison(materialRequestId) {
    const res = await fetch(`${API_BASE_URL}/procurement/comparison/${materialRequestId}`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch quotation comparison matrix');
    return await res.json();
  },

  async evaluateWithAI(materialRequestId) {
    const res = await fetch(`${API_BASE_URL}/procurement/${materialRequestId}/evaluate`, {
      method: 'POST',
      headers: getHeaders()
    });
    handleUnauthorized(res);
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || 'Failed to run AI Supplier Evaluation Agent');
    }
    return await res.json();
  },

  async getRecommendations() {
    const res = await fetch(`${API_BASE_URL}/procurement/recommendations`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch procurement recommendations');
    return await res.json();
  },

  async approveRecommendation(recommendationId, payload) {
    const res = await fetch(`${API_BASE_URL}/procurement/recommendations/${recommendationId}/approve`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(payload)
    });
    handleUnauthorized(res);
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || 'Failed to authorize procurement recommendation');
    }
    return await res.json();
  },

  async getPurchaseOrders() {
    const res = await fetch(`${API_BASE_URL}/procurement/purchase-orders`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch purchase orders');
    return await res.json();
  }
};

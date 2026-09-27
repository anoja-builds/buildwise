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

export const deliveryService = {
  async getExpectedDeliveries() {
    const res = await fetch(`${API_BASE_URL}/deliveries/expected`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch expected deliveries');
    return res.json();
  },

  async getDeliveryHistory() {
    const res = await fetch(`${API_BASE_URL}/deliveries/history`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch delivery history');
    return res.json();
  },

  async getDeliveryById(id) {
    const res = await fetch(`${API_BASE_URL}/deliveries/${id}`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch delivery details');
    return res.json();
  },

  async receiveDelivery(id, data) {
    const res = await fetch(`${API_BASE_URL}/deliveries/${id}/receive`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(data)
    });
    handleUnauthorized(res);
    if (!res.ok) {
      const errorMsg = await res.text();
      throw new Error(errorMsg || 'Failed to reconcile delivery');
    }
    return res.json();
  },

  async uploadEvidence(id, imageUrl) {
    const res = await fetch(`${API_BASE_URL}/deliveries/${id}/evidence`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify({ imageUrl })
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to upload photographic evidence');
    return res.json();
  },

  async evaluateRisk(purchaseOrderId, userId) {
    const query = userId ? `?userId=${userId}` : '';
    const res = await fetch(`${API_BASE_URL}/deliveries/evaluate-risk/${purchaseOrderId}${query}`, {
      method: 'POST',
      headers: getHeaders()
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to run Delivery Risk Agent');
    return res.json();
  }
};

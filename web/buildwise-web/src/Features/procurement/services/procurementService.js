const API_BASE_URL = 'http://localhost:5078/api';

export const procurementService = {
  async getSuppliers(status = '') {
    let url = `${API_BASE_URL}/suppliers`;
    if (status) url += `?status=${status}`;
    const res = await fetch(url);
    if (!res.ok) throw new Error('Failed to fetch suppliers');
    return await res.json();
  },

  async createSupplier(payload) {
    const res = await fetch(`${API_BASE_URL}/suppliers`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Failed to create supplier');
    return await res.json();
  },

  async getRfqs() {
    const res = await fetch(`${API_BASE_URL}/procurement/rfqs`);
    if (!res.ok) throw new Error('Failed to fetch RFQs');
    return await res.json();
  },

  async createRfq(payload) {
    const res = await fetch(`${API_BASE_URL}/procurement/rfqs`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Failed to create RFQ');
    return await res.json();
  },

  async submitQuotation(payload) {
    const res = await fetch(`${API_BASE_URL}/procurement/quotations`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Failed to record supplier quotation');
    return await res.json();
  },

  async getQuotationComparison(materialRequestId) {
    const res = await fetch(`${API_BASE_URL}/procurement/comparison/${materialRequestId}`);
    if (!res.ok) throw new Error('Failed to fetch quotation comparison matrix');
    return await res.json();
  },

  async evaluateWithAI(materialRequestId) {
    const res = await fetch(`${API_BASE_URL}/procurement/${materialRequestId}/evaluate`, {
      method: 'POST'
    });
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || 'Failed to run AI Supplier Evaluation Agent');
    }
    return await res.json();
  },

  async getRecommendations() {
    const res = await fetch(`${API_BASE_URL}/procurement/recommendations`);
    if (!res.ok) throw new Error('Failed to fetch procurement recommendations');
    return await res.json();
  },

  async approveRecommendation(recommendationId, payload) {
    const res = await fetch(`${API_BASE_URL}/procurement/recommendations/${recommendationId}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || 'Failed to authorize procurement recommendation');
    }
    return await res.json();
  },

  async getPurchaseOrders() {
    const res = await fetch(`${API_BASE_URL}/procurement/purchase-orders`);
    if (!res.ok) throw new Error('Failed to fetch purchase orders');
    return await res.json();
  }
};

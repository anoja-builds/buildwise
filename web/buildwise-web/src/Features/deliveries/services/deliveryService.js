const API_BASE_URL = 'http://localhost:5078/api';

export const deliveryService = {
  async getExpectedDeliveries() {
    const res = await fetch(`${API_BASE_URL}/deliveries/expected`);
    if (!res.ok) throw new Error('Failed to fetch expected deliveries');
    return res.json();
  },

  async getDeliveryHistory() {
    const res = await fetch(`${API_BASE_URL}/deliveries/history`);
    if (!res.ok) throw new Error('Failed to fetch delivery history');
    return res.json();
  },

  async getDeliveryById(id) {
    const res = await fetch(`${API_BASE_URL}/deliveries/${id}`);
    if (!res.ok) throw new Error('Failed to fetch delivery details');
    return res.json();
  },

  async receiveDelivery(id, data) {
    const res = await fetch(`${API_BASE_URL}/deliveries/${id}/receive`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      const errorMsg = await res.text();
      throw new Error(errorMsg || 'Failed to reconcile delivery');
    }
    return res.json();
  },

  async uploadEvidence(id, imageUrl) {
    const res = await fetch(`${API_BASE_URL}/deliveries/${id}/evidence`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ imageUrl })
    });
    if (!res.ok) throw new Error('Failed to upload photographic evidence');
    return res.json();
  },

  async evaluateRisk(purchaseOrderId, userId) {
    const query = userId ? `?userId=${userId}` : '';
    const res = await fetch(`${API_BASE_URL}/deliveries/evaluate-risk/${purchaseOrderId}${query}`, {
      method: 'POST'
    });
    if (!res.ok) throw new Error('Failed to run Delivery Risk Agent');
    return res.json();
  }
};

const API_BASE_URL = 'http://localhost:5078/api';

export const materialRequestService = {
  async getRequests(status = '', projectId = '') {
    let url = `${API_BASE_URL}/materialrequests`;
    const params = new URLSearchParams();
    if (status) params.append('status', status);
    if (projectId) params.append('projectId', projectId);
    if (params.toString()) url += `?${params.toString()}`;

    const res = await fetch(url);
    if (!res.ok) throw new Error('Failed to fetch material requests');
    return await res.json();
  },

  async getRequestById(id) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}`);
    if (!res.ok) throw new Error(`Failed to fetch material request #${id}`);
    return await res.json();
  },

  async createRequest(payload) {
    const res = await fetch(`${API_BASE_URL}/materialrequests`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      const err = await res.text();
      throw new Error(err || 'Failed to create material request');
    }
    return await res.json();
  },

  async submitRequest(id) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}/submit`, {
      method: 'POST'
    });
    if (!res.ok) throw new Error('Failed to submit material request');
    return await res.json();
  },

  async approveRequest(id, payload) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Failed to record approval decision');
    return await res.json();
  },

  async runPlanningAgent(id) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}/plan`, {
      method: 'POST'
    });
    if (!res.ok) throw new Error('Failed to execute Procurement Planning Agent AI');
    return await res.json();
  }
};

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

export const materialRequestService = {
  async getRequests(status = '', projectId = '') {
    let url = `${API_BASE_URL}/materialrequests`;
    const params = new URLSearchParams();
    if (status) params.append('status', status);
    if (projectId) params.append('projectId', projectId);
    if (params.toString()) url += `?${params.toString()}`;

    const res = await fetch(url, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to fetch material requests');
    return await res.json();
  },

  async getRequestById(id) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}`, { headers: getHeaders() });
    handleUnauthorized(res);
    if (!res.ok) throw new Error(`Failed to fetch material request #${id}`);
    return await res.json();
  },

  async createRequest(payload) {
    const res = await fetch(`${API_BASE_URL}/materialrequests`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(payload)
    });
    handleUnauthorized(res);
    if (!res.ok) {
      const err = await res.text();
      throw new Error(err || 'Failed to create material request');
    }
    return await res.json();
  },

  async submitRequest(id) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}/submit`, {
      method: 'POST',
      headers: getHeaders()
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to submit material request');
    return await res.json();
  },

  async approveRequest(id, payload) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}/approve`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify(payload)
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to record approval decision');
    return await res.json();
  },

  async runPlanningAgent(id) {
    const res = await fetch(`${API_BASE_URL}/materialrequests/${id}/plan`, {
      method: 'POST',
      headers: getHeaders()
    });
    handleUnauthorized(res);
    if (!res.ok) throw new Error('Failed to execute Procurement Planning Agent AI');
    return await res.json();
  }
};

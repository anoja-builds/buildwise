// Thin wrapper around fetch for the real ASP.NET Core backend.
// Not used yet (see materialRequestApi.js, which currently runs
// against in-memory mock data) — this is here so swapping the mock
// layer for the real API later is a one-file change.

const BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api';

function getToken() {
  return localStorage.getItem('buildwise_jwt');
}

async function request(path, { method = 'GET', body, params } = {}) {
  const url = new URL(BASE_URL + path, window.location.origin);
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        url.searchParams.set(key, value);
      }
    });
  }

  const token = getToken();
  const res = await fetch(url.toString(), {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const errBody = await res.json();
      message = errBody.message || message;
    } catch {
      /* response wasn't JSON */
    }
    throw new Error(message);
  }

  if (res.status === 204) return null;
  return res.json();
}

export const apiClient = {
  get: (path, params) => request(path, { method: 'GET', params }),
  post: (path, body) => request(path, { method: 'POST', body }),
  put: (path, body) => request(path, { method: 'PUT', body }),
  patch: (path, body) => request(path, { method: 'PATCH', body }),
  del: (path) => request(path, { method: 'DELETE' }),
};

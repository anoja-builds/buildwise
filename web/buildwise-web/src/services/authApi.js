const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api'
const STORAGE_KEY = 'buildwise.auth'

async function request(path, body) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  })

  if (!res.ok) {
    let message = `Request failed (${res.status})`
    try {
      const data = await res.json()
      message = data?.error || message
    } catch {
      // no JSON body
    }
    throw new Error(message)
  }

  return res.json()
}

export const authApi = {
  login: (email, password) => request('/auth/login', { email, password }),
  register: (fullName, email, password, roleName) => request('/auth/register', { fullName, email, password, roleName }),

  loadSession: () => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY)
      return raw ? JSON.parse(raw) : null
    } catch {
      return null
    }
  },
  saveSession: (session) => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
    } catch {
      // localStorage unavailable (private mode, etc.) — session just won't persist across reloads
    }
  },
  clearSession: () => {
    try {
      localStorage.removeItem(STORAGE_KEY)
    } catch {
      // ignore
    }
  }
}

export default authApi

import { describe, expect, it, vi, afterEach } from 'vitest'
import { API_BASE, isNetworkFailure, networkFailureMessage, fetchOrThrow } from '../services/apiTransport'

describe('apiTransport', () => {
  afterEach(() => vi.restoreAllMocks())

  // Regression: the app used to surface the browser's bare "Failed to fetch" with
  // no indication that the API was simply not running.
  it('turns a network failure into an actionable message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    await expect(fetchOrThrow(`${API_BASE}/suppliers`)).rejects.toThrow(/Cannot reach the BuildWise API/)
  })

  it('names the unreachable URL and the fix', () => {
    const message = networkFailureMessage(new TypeError('Failed to fetch'), 'http://localhost:5078/api')
    expect(message).toContain('http://localhost:5078/api')
    expect(message).toContain('start-dev.ps1')
  })

  it('recognises a TypeError as a network failure', () => {
    expect(isNetworkFailure(new TypeError('Failed to fetch'))).toBe(true)
  })

  it('recognises the browser message variants as a network failure', () => {
    expect(isNetworkFailure(new Error('NetworkError when attempting to fetch resource.'))).toBe(true)
    expect(isNetworkFailure(new Error('Load failed'))).toBe(true)
  })

  it('does not treat an ordinary error as a network failure', () => {
    expect(isNetworkFailure(new Error('Request failed (500)'))).toBe(false)
    expect(isNetworkFailure(new Error('Your session has expired. Please sign in again.'))).toBe(false)
  })

  it('leaves HTTP-level errors untouched so callers keep handling 401/403', async () => {
    const fakeResponse = { ok: false, status: 403, json: async () => ({ message: 'Forbidden' }) }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(fakeResponse))

    const res = await fetchOrThrow(`${API_BASE}/admin/users`)
    expect(res.status).toBe(403)
  })

  it('returns a normal response untouched', async () => {
    const fakeResponse = { ok: true, status: 200, json: async () => ({ items: [] }) }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(fakeResponse))

    const res = await fetchOrThrow(`${API_BASE}/suppliers`)
    expect(res.status).toBe(200)
  })
})
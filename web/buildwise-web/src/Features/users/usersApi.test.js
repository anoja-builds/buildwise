import { afterEach, expect, it, vi } from 'vitest'
import { usersApi } from './usersApi'
import { authApi } from '../../services/authApi'

afterEach(() => { vi.unstubAllGlobals(); authApi.clearSession() })

it('sends authenticated API requests and parses validation errors', async () => {
  authApi.saveSession({ token: 'test-token' })
  const fetch = vi.fn().mockResolvedValueOnce({ ok: true, status: 200, json: async () => [] })
    .mockResolvedValueOnce({ ok: false, status: 400, json: async () => ({ errors: { Password: ['Password must be at least 8 characters.'] } }) })
    .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ id: 2, isActive: false }) })
  vi.stubGlobal('fetch', fetch)
  await usersApi.list()
  expect(fetch).toHaveBeenLastCalledWith(expect.stringMatching(/\/api\/users$/), expect.objectContaining({ headers: { Authorization: 'Bearer test-token' } }))
  await expect(usersApi.create({ password: 'short' })).rejects.toThrow('Password must be at least 8 characters.')
  await usersApi.setStatus(2, false)
  expect(fetch).toHaveBeenLastCalledWith(expect.stringMatching(/\/users\/2\/status$/), expect.objectContaining({ method: 'PATCH', body: '{"isActive":false}' }))
})

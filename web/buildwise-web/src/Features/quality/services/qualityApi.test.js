import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authApi } from '../../../services/authApi'
import { qualityApi } from './qualityApi'

beforeEach(() => { vi.stubGlobal('fetch', vi.fn()); authApi.saveSession({ token: 'quality-jwt' }) })
afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.restoreAllMocks(); localStorage.clear() })
const response = (status, data) => ({ status, ok: status < 400, json: async () => data })

describe('quality API', () => {
  it('uses VITE_API_BASE_URL including a custom local address', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://127.0.0.1:60001/api/')
    vi.resetModules()
    const { qualityApi: configuredApi } = await import('./qualityApi')
    fetch.mockResolvedValue(response(200, []))
    await configuredApi.listInspections()
    expect(fetch.mock.calls[0][0]).toBe('http://127.0.0.1:60001/api/inspections')
  })
  it.each([
    ['listInspections', [], '/inspections', 'GET'], ['getInspection', [4], '/inspections/4', 'GET'],
    ['listNcrs', [], '/non-conformances', 'GET'], ['getNcr', [5], '/non-conformances/5', 'GET'],
    ['createNcr', [{ inspectionItemId: 9 }], '/non-conformances', 'POST'],
    ['updateCorrectiveAction', [5, 'Replace'], '/non-conformances/5/corrective-action', 'PATCH'],
    ['resolveNcr', [5], '/non-conformances/5/resolve', 'POST'], ['closeNcr', [5], '/non-conformances/5/close', 'POST'],
    ['analyseInspection', [4], '/quality-risk-agent/inspections/4/analyse', 'POST'], ['getWorkflow', [20], '/quality-risk-agent/workflows/20', 'GET'],
  ])('%s uses the real authenticated endpoint', async (method, args, path, verb) => {
    fetch.mockResolvedValue(response(200, { id: 5 }))
    await qualityApi[method](...args)
    expect(fetch).toHaveBeenCalledWith(expect.stringContaining(`/api${path}`), expect.objectContaining({ method: verb, headers: expect.objectContaining({ Authorization: 'Bearer quality-jwt' }) }))
    if (method === 'updateCorrectiveAction') expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ correctiveAction: 'Replace' })
  })
  it('clears shared authentication and emits the existing event on 401', async () => {
    fetch.mockResolvedValue(response(401, {}))
    const listener = vi.fn(); window.addEventListener('buildwise:unauthorized', listener)
    await expect(qualityApi.listInspections()).rejects.toThrow('session has expired')
    expect(authApi.loadSession()).toBeNull(); expect(listener).toHaveBeenCalledOnce()
    window.removeEventListener('buildwise:unauthorized', listener)
  })
  it.each([[403, {}, 'not authorized'], [409, { detail: 'Only resolved records can be closed.' }, 'Only resolved'], [400, { errors: { Severity: ['Invalid severity.'] } }, 'Invalid severity']])('surfaces HTTP %s', async (status, data, message) => {
    fetch.mockResolvedValue(response(status, data))
    await expect(qualityApi.listNcrs()).rejects.toThrow(message)
  })
  it('reports network failure without a mock fallback', async () => {
    fetch.mockRejectedValue(new TypeError('Failed to fetch'))
    await expect(qualityApi.listInspections()).rejects.toThrow('Unable to reach the API')
  })
  it('preserves the persisted failure workflow in a 502 response', async () => {
    const failed = { workflowId: 20, inspectionId: 4, status: 'Failed', steps: [] }
    fetch.mockResolvedValue(response(502, failed))
    await expect(qualityApi.analyseInspection(4)).rejects.toMatchObject({ status: 502, data: failed })
  })
})

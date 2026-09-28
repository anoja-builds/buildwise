import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { procurementApi } from './procurementApi'
import { authApi } from '../../../services/authApi'
vi.mock('../../../services/authApi', () => ({ authApi: { loadSession: vi.fn(), clearSession: vi.fn() } }))
beforeEach(() => { vi.clearAllMocks(); authApi.loadSession.mockReturnValue({ token: 'jwt' }); vi.stubGlobal('fetch', vi.fn()) })
afterEach(() => vi.unstubAllGlobals())
it('uses the Component 1 route and maps the real response fields', async () => {
  fetch.mockResolvedValueOnce(new Response(JSON.stringify([{ id: 7, status: 'Approved' }]), { status: 200 }))
  await procurementApi.listApprovedMaterialRequests()
  expect(fetch.mock.calls[0][0]).toMatch(/\/MaterialRequests\?status=Approved$/)
  expect(fetch.mock.calls[0][1].headers.Authorization).toBe('Bearer jwt')
  fetch.mockResolvedValueOnce(new Response(JSON.stringify({ id: 7, items: [{ id: 92, quantity: 250, materialUnit: 'bags' }] }), { status: 200 }))
  const detail = await procurementApi.getMaterialRequest(7)
  expect(detail.items[0]).toMatchObject({ id: 92, requestedQuantity: 250, unit: 'bags' })
})
it.each(['listSuppliers', 'listApprovedMaterialRequests', 'startWorkflow', 'getWorkflow', 'listPurchaseOrders'])('%s never fabricates success on network failure', async method => {
  fetch.mockRejectedValue(new TypeError('Failed to fetch'))
  await expect(procurementApi[method](7)).rejects.toThrow('Cannot reach BuildWise API')
})
it('401 clears session and never supplies fixtures, even without a token', async () => {
  authApi.loadSession.mockReturnValue(null)
  fetch.mockResolvedValue(new Response('', { status: 401 }))
  await expect(procurementApi.listSuppliers()).rejects.toThrow('sign in again')
  expect(authApi.clearSession).toHaveBeenCalled()
})
it('sends no client reviewer identity and surfaces backend rejection', async () => {
  fetch.mockResolvedValue(new Response('Only manager approval is allowed', { status: 403 }))
  await expect(procurementApi.recordDecision(8, 'Approve', 'Reviewed')).rejects.toThrow('Only manager approval')
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ decision: 'Approve', comment: 'Reviewed' })
})

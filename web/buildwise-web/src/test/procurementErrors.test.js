import { afterEach, expect, it, vi } from 'vitest'
import { procurementApi } from '../Features/procurement/services/procurementApi'

vi.mock('../services/authApi', () => ({ authApi: { loadSession: () => ({ token: 'test' }) } }))
afterEach(() => vi.unstubAllGlobals())

it.each([
  [{ message: 'Only approved material requests can be used.' }, 'Only approved material requests can be used.'],
  [{ title: 'Validation failed', errors: { SupplierIds: ['Select at least one supplier.'] } }, 'Select at least one supplier.'],
])('shows the actual procurement validation reason', async (payload, expected) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 400, json: async () => payload }))
  await expect(procurementApi.createRfq({})).rejects.toMatchObject({ message: expected, status: 400, payload })
})

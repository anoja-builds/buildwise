import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../../services/authApi';
import { deliveryService } from './deliveryService';

vi.mock('../../../services/authApi', () => ({
  authApi: { loadSession: vi.fn(), clearSession: vi.fn() }
}));

const apiBase = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api').replace(/\/+$/, '');

describe.each([
  ['analyzeDiscrepancies', 'discrepancy-analysis', 'POST'],
  ['getDiscrepancyHistory', 'discrepancy-history', 'GET']
])('deliveryService.%s', (method, endpoint, verb) => {
  beforeEach(() => {
    vi.clearAllMocks();
    authApi.loadSession.mockReturnValue({ token: 'test-token' });
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('calls the authenticated endpoint and returns the backend JSON', async () => {
    const result = method === 'analyzeDiscrepancies' ? { workflowId: 7 } : [{ workflowId: 7 }];
    fetch.mockResolvedValue({ ok: true, status: 200, json: async () => result });

    expect(await deliveryService[method](42)).toEqual(result);
    expect(fetch).toHaveBeenCalledExactlyOnceWith(`${apiBase}/deliveries/42/${endpoint}`, {
      ...(verb === 'POST' ? { method: 'POST' } : {}),
      headers: { Authorization: 'Bearer test-token' }
    });
  });

  it('preserves the backend error without a fallback result', async () => {
    fetch.mockResolvedValue({ ok: false, status: 400, text: async () => 'Delivery has not been received yet.' });
    await expect(deliveryService[method](42)).rejects.toThrow('Delivery has not been received yet.');
  });

  it('uses the existing unauthorized handler', async () => {
    const dispatch = vi.spyOn(window, 'dispatchEvent');
    fetch.mockResolvedValue({ ok: false, status: 401 });
    await expect(deliveryService[method](42)).rejects.toThrow('Your session has expired. Please sign in again.');
    expect(authApi.clearSession).toHaveBeenCalledOnce();
    expect(dispatch).toHaveBeenCalledWith(expect.objectContaining({ type: 'buildwise:unauthorized' }));
  });
});

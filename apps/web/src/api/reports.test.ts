import { afterEach, expect, it, vi } from 'vitest';
import { clearSession, getToken, saveSession } from '../auth/session';
import { reportsApi } from './reports';

afterEach(() => { vi.unstubAllGlobals(); clearSession(); });
const query = { from: '2026-10-01T00:00:00.000Z', to: '2026-11-01T00:00:00.000Z', groupBy: 'priority' as const };
function login() {
  saveSession({ token: 'report-test-token', expiresAtUtc: new Date(Date.now() + 60000).toISOString() });
}

it('serializes report parameters and uses the shared bearer client and abort signal', async () => {
  login();
  const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response('[]', { status: 200 }));
  vi.stubGlobal('fetch', fetchMock);
  const controller = new AbortController();
  await reportsApi.getSlaSummary(query, controller.signal);
  const [input, init] = fetchMock.mock.calls[0];
  const url = new URL(String(input), 'http://localhost');
  expect(url.pathname).toBe('/api/reports/sla-summary');
  expect(Object.fromEntries(url.searchParams)).toEqual(query);
  expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer report-test-token');
  expect(init?.signal).toBe(controller.signal);
});

it('lets a report 401 clear the session through the central client', async () => {
  login();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('expired', { status: 401 })));
  await expect(reportsApi.getSlaSummary(query)).rejects.toMatchObject({ status: 401 });
  expect(getToken()).toBeNull();
});

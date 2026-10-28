import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearSession, getToken, saveSession, sessionKey } from '../auth/session';
import { request, ApiError } from './client';

const expiry = new Date(Date.now() + 3600000).toISOString();
beforeEach(() => { clearSession(); vi.stubGlobal('fetch', vi.fn()); });
afterEach(() => { clearSession(); vi.unstubAllGlobals(); });
describe('authenticated API client', () => {
  it('attaches the token and preserves caller headers', async () => {
    saveSession({ token: 'access-token', expiresAtUtc: expiry });
    vi.mocked(fetch).mockResolvedValue(new Response('{}', { status: 200 }));
    await request('/tickets', { headers: [['X-Request-Test', 'present']] });
    const headers = new Headers(vi.mocked(fetch).mock.calls[0][1]?.headers);
    expect(headers.get('Authorization')).toBe('Bearer access-token');
    expect(headers.get('X-Request-Test')).toBe('present');
  });
  it('does not attach an old token to a public login request', async () => {
    saveSession({ token: 'old', expiresAtUtc: expiry });
    vi.mocked(fetch).mockResolvedValue(new Response('{}', { status: 200 }));
    await request('/auth/login', { method: 'POST' }, { anonymous: true });
    expect(new Headers(vi.mocked(fetch).mock.calls[0][1]?.headers).has('Authorization')).toBe(false);
  });
  it('a non-JSON 401 still clears the persisted session and throws ApiError', async () => {
    saveSession({ token: 'expired', expiresAtUtc: expiry });
    vi.mocked(fetch).mockResolvedValue(new Response('expired', { status: 401 }));
    await expect(request('/tickets')).rejects.toMatchObject({ status: 401, name: 'ApiError' });
    expect(getToken()).toBeNull();
    expect(sessionStorage.getItem(sessionKey)).toBeNull();
  });
  it('other server errors preserve the session', async () => {
    saveSession({ token: 'valid', expiresAtUtc: expiry });
    vi.mocked(fetch).mockResolvedValue(new Response('{}', { status: 500 }));
    await expect(request('/tickets')).rejects.toBeInstanceOf(ApiError);
    expect(getToken()).toBe('valid');
  });
  it('a stale request cannot clear a newer successful login', async () => {
    saveSession({ token: 'old', expiresAtUtc: expiry });
    let finish: ((value: Response) => void) | undefined;
    vi.mocked(fetch).mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    const pending = request('/tickets').catch((error: unknown) => error);
    saveSession({ token: 'new', expiresAtUtc: expiry });
    finish?.(new Response('', { status: 401 }));
    await pending;
    expect(getToken()).toBe('new');
  });
});

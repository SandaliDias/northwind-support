import { beforeEach, describe, expect, it, vi } from 'vitest';
beforeEach(() => { sessionStorage.clear(); vi.resetModules(); });
describe('stored session initialization', () => {
  it.each(['not json', '{}', JSON.stringify({ token: 'expired', expiresAtUtc: '2000-01-01T00:00:00Z' })])('discards invalid or expired storage', async (value) => {
    sessionStorage.setItem('northwind.auth', value);
    const store = await import('./session');
    expect(store.getToken()).toBeNull();
    expect(sessionStorage.getItem('northwind.auth')).toBeNull();
  });
});

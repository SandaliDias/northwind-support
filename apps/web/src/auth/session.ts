export interface StoredSession { token: string; expiresAtUtc: string }
export const sessionKey = 'northwind.auth';

function readStoredSession(): StoredSession | null {
  try {
    const raw = sessionStorage.getItem(sessionKey);
    if (!raw) return null;
    const value: unknown = JSON.parse(raw);
    if (typeof value === 'object' && value !== null && 'token' in value && 'expiresAtUtc' in value
      && typeof value.token === 'string' && value.token.length > 0 && typeof value.expiresAtUtc === 'string'
      && Date.parse(value.expiresAtUtc) > Date.now()) {
      return { token: value.token, expiresAtUtc: value.expiresAtUtc };
    }
    sessionStorage.removeItem(sessionKey);
  } catch {
    try { sessionStorage.removeItem(sessionKey); } catch { /* Browser storage is unavailable. */ }
  }
  return null;
}

let session = readStoredSession();
const listeners = new Set<() => void>();
export const getSession = () => session;
export const getToken = () => session?.token ?? null;
export function subscribeSession(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
export function saveSession(value: StoredSession) {
  session = { token: value.token, expiresAtUtc: value.expiresAtUtc };
  try { sessionStorage.setItem(sessionKey, JSON.stringify(session)); } catch { /* Fall back to this tab's memory. */ }
  listeners.forEach((listener) => listener());
}
export function clearSession(expectedToken?: string | null) {
  // An old request's 401 cannot erase a newer login session.
  if (expectedToken !== undefined && expectedToken !== getToken()) return;
  session = null;
  try { sessionStorage.removeItem(sessionKey); } catch { /* Storage may be unavailable. */ }
  listeners.forEach((listener) => listener());
}

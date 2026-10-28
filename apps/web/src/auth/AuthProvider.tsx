import { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { authApi } from '../api/auth';
import { toErrorMessage } from '../api/client';
import type { AuthUser, LoginPayload } from '../types/api';
import { AuthContext, type AuthState } from './AuthContext';
import { clearSession, getSession, saveSession, subscribeSession } from './session';

interface Validation { token: string; user?: AuthUser; error?: string }

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const session = useSyncExternalStore(subscribeSession, getSession);
  const [validation, setValidation] = useState<Validation>();
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!session) return;
    const controller = new AbortController();
    void authApi.me(controller.signal).then((user) => {
      if (!controller.signal.aborted && getSession()?.token === session.token)
        setValidation({ token: session.token, user });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted && getSession()?.token === session.token)
        setValidation({ token: session.token, error: toErrorMessage(error, 'Unable to validate your session.') });
    });
    return () => controller.abort();
  }, [session, attempt]);

  const signIn = useCallback(async (payload: LoginPayload) => {
    const result = await authApi.login(payload);
    setValidation({ token: result.token, user: result.user });
    saveSession(result);
  }, []);
  const logout = useCallback(() => {
    setValidation(undefined);
    clearSession();
  }, []);
  const retry = useCallback(() => {
    setValidation(undefined);
    setAttempt((value) => value + 1);
  }, []);
  const current = session && validation?.token === session.token ? validation : undefined;
  const status: AuthState['status'] = !session ? 'anonymous' : current?.user ? 'authenticated'
    : current?.error ? 'error' : 'checking';
  const value = useMemo<AuthState>(() => ({ status, user: current?.user ?? null, error: current?.error,
    signIn, logout, retry }), [status, current, signIn, logout, retry]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

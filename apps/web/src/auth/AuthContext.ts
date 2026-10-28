import { createContext, useContext } from 'react';
import type { AuthUser, LoginPayload } from '../types/api';

export interface AuthState {
  status: 'anonymous' | 'checking' | 'authenticated' | 'error';
  user: AuthUser | null;
  error?: string;
  signIn: (payload: LoginPayload) => Promise<void>;
  logout: () => void;
  retry: () => void;
}
export const AuthContext = createContext<AuthState | null>(null);
export function useAuth() {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('AuthProvider is required.');
  return auth;
}

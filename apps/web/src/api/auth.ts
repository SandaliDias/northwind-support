import type { AuthUser, LoginPayload, LoginResult } from '../types/api';
import { request } from './client';

export const authApi = {
  login: (payload: LoginPayload) => request<LoginResult>('/auth/login', {
    method: 'POST', body: JSON.stringify(payload),
  }, { anonymous: true }),
  me: (signal?: AbortSignal) => request<AuthUser>('/auth/me', { signal }),
};

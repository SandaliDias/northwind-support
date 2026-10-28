import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { authApi } from '../api/auth';
import { ApiError, request } from '../api/client';
import { LoginPage } from '../features/auth/LoginPage';
import type { AuthUser, LoginResult } from '../types/api';
import { AuthProvider } from './AuthProvider';
import { ProtectedRoute } from './ProtectedRoute';
import { clearSession, getToken, saveSession, sessionKey } from './session';
import { returnPath } from './returnPath';

vi.mock('../api/auth', () => ({ authApi: { login: vi.fn(), me: vi.fn() } }));
const user: AuthUser = { id: 1, fullName: 'Alex Turner', email: 'alex.turner@northwind-support.example' };
const loginResult: LoginResult = { token: 'valid-token', expiresAtUtc: new Date(Date.now() + 3600000).toISOString(), user };
function Area() {
  const location = useLocation();
  return <><h1>Private tickets</h1><output>{location.pathname + location.search}</output>
    <button onClick={() => void request('/tickets').catch(() => undefined)}>Load data</button></>;
}
function show(path = '/tickets') {
  render(<AuthProvider><MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<ProtectedRoute />}><Route element={<App />}>
      <Route path="/tickets" element={<Area />} /><Route path="/tickets/:id" element={<Area />} />
    </Route></Route>
  </Routes></MemoryRouter></AuthProvider>);
}
function enter() {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'alex.turner@northwind-support.example' } });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: '  password with spaces  ' } });
}
beforeEach(() => {
  clearSession(); sessionStorage.clear(); vi.resetAllMocks();
  vi.mocked(authApi.me).mockResolvedValue(user);
  vi.mocked(authApi.login).mockResolvedValue(loginResult);
});
afterEach(() => { cleanup(); clearSession(); vi.unstubAllGlobals(); });

describe('authentication flow', () => {
  it('redirects anonymous visitors and does not render private content', async () => {
    show('/tickets/42?tab=history');
    expect(await screen.findByLabelText('Email')).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Private tickets' })).not.toBeInTheDocument();
    expect(authApi.me).not.toHaveBeenCalled();
  });
  it('validates locally without calling login', () => {
    show('/login'); fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByText('Enter a valid email address.')).toBeVisible();
    expect(screen.getByText('Enter a password of 1-1024 characters.')).toBeVisible();
    expect(authApi.login).not.toHaveBeenCalled();
  });
  it('disables pending login then returns to the requested page and greets the agent', async () => {
    let finish: ((result: LoginResult) => void) | undefined;
    vi.mocked(authApi.login).mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    show('/tickets/42?tab=history'); enter();
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByRole('button', { name: 'Signing in...' })).toBeDisabled();
    expect(authApi.login).toHaveBeenCalledWith({ email: user.email, password: '  password with spaces  ' });
    await act(async () => { finish?.(loginResult); });
    expect(await screen.findByText('/tickets/42?tab=history')).toBeVisible();
    expect(screen.getByText('Signed in as Alex Turner')).toBeVisible();
    expect(JSON.parse(sessionStorage.getItem(sessionKey) ?? '{}')).toEqual({ token: loginResult.token, expiresAtUtc: loginResult.expiresAtUtc });
  });
  it('shows rejected credentials and keeps the session anonymous', async () => {
    vi.mocked(authApi.login).mockRejectedValue(new ApiError(401, 'Invalid email or password.'));
    show('/login'); enter(); fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.');
    expect(getToken()).toBeNull();
    expect(screen.getByLabelText('Email')).toBeVisible();
  });
  it('checks a restored session with me before rendering protected content', async () => {
    let finish: ((value: AuthUser) => void) | undefined;
    vi.mocked(authApi.me).mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    saveSession(loginResult); show();
    expect(screen.queryByRole('heading', { name: 'Private tickets' })).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Checking your session');
    await act(async () => { finish?.(user); });
    expect(await screen.findByRole('heading', { name: 'Private tickets' })).toBeVisible();
  });
  it('logout clears storage and removes the agent and private content', async () => {
    saveSession(loginResult); show();
    await screen.findByRole('heading', { name: 'Private tickets' });
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    expect(await screen.findByLabelText('Email')).toBeVisible();
    expect(sessionStorage.getItem(sessionKey)).toBeNull();
    expect(screen.queryByText('Signed in as Alex Turner')).not.toBeInTheDocument();
  });
  it('a 401 from any protected request clears the session and redirects to login', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('expired', { status: 401 })));
    saveSession(loginResult); show('/tickets/42');
    await screen.findByRole('heading', { name: 'Private tickets' });
    fireEvent.click(screen.getByRole('button', { name: 'Load data' }));
    expect(await screen.findByLabelText('Email')).toBeVisible();
    expect(getToken()).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Private tickets' })).not.toBeInTheDocument();
  });
  it('offers retry for a temporary session validation failure', async () => {
    vi.mocked(authApi.me).mockRejectedValueOnce(new Error('Network unavailable')).mockResolvedValue(user);
    saveSession(loginResult); show();
    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to validate your session.');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Private tickets' })).toBeVisible());
  });
});

describe('login destination', () => {
  it.each(['https://evil.example', '//evil.example', '/\\evil.example', '/login', '/login?next=x', '/tickets\n'])('rejects unsafe or looping destination %s', (path) => {
    expect(returnPath(path)).toBe('/tickets');
  });
  it('preserves an internal path and query', () => expect(returnPath('/tickets/42?tab=history')).toBe('/tickets/42?tab=history'));
});

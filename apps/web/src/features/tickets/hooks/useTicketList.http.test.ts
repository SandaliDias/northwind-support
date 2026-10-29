import { act, cleanup, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearSession } from '../../../auth/session';
import { page } from '../../../test/fixtures';
import type { TicketFilters } from '../../../types/api';
import { useTicketList } from './useTicketList';

// Keep the real hook -> ticketsApi -> request -> fetch chain; stub only the network boundary.
beforeEach(() => {
  vi.useFakeTimers();
  clearSession();
  vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockImplementation(async () =>
    new Response(JSON.stringify(page([])), { status: 200 })));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  clearSession();
});

const changes: Array<{ key: keyof TicketFilters; value: string; patch: Partial<TicketFilters> }> = [
  { key: 'status', value: 'InProgress', patch: { status: 'InProgress' } },
  { key: 'slaStatus', value: 'AtRisk', patch: { slaStatus: 'AtRisk' } },
];

describe('DEFECT-117 real HTTP requests', () => {
  it.each(changes)('sends $key immediately without typing in search', async ({ key, value, patch }) => {
    const { result } = renderHook(() => useTicketList());
    // Allow either implementation to finish initial loading; isolate the dropdown regression.
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(fetch).toHaveBeenCalledTimes(1);

    await act(async () => { result.current.updateFilters(patch); });
    expect(fetch).toHaveBeenCalledTimes(2);
    const [input, init] = vi.mocked(fetch).mock.calls[1];
    const url = new URL(String(input), 'http://localhost');
    expect(url.pathname).toBe('/api/tickets');
    expect(init?.method ?? 'GET').toBe('GET');
    expect(url.searchParams.get(key)).toBe(value);
    expect(url.searchParams.get('page')).toBe('1');
  });

  it('debounces keystrokes and sends a request without search when it is cleared', async () => {
    const { result } = renderHook(() => useTicketList());
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    act(() => { result.current.updateFilters({ search: 'a' }); });
    await act(async () => { await vi.advanceTimersByTimeAsync(100); });
    act(() => { result.current.updateFilters({ search: 'ab' }); });
    expect(fetch).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(new URL(String(vi.mocked(fetch).mock.calls[1][0]), 'http://localhost').searchParams.get('search')).toBe('ab');

    act(() => { result.current.updateFilters({ search: '' }); });
    expect(fetch).toHaveBeenCalledTimes(2);
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(new URL(String(vi.mocked(fetch).mock.calls[2][0]), 'http://localhost').searchParams.has('search')).toBe(false);
  });
});

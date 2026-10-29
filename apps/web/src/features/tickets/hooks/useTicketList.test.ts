import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import { useTicketList } from './useTicketList';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    let result: { current: ReturnType<typeof useTicketList> };
    await act(async () => {
      const rendered = renderHook(() => useTicketList());
      result = rendered.result;
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', page: 1 }),
    );

    expect(result!.current.isLoading).toBe(false);
    expect(result!.current.data?.items).toHaveLength(1);
  });

  it('DEFECT-117: triggers an immediate request when changing status dropdown', async () => {
    let result: { current: ReturnType<typeof useTicketList> };
    await act(async () => {
      const rendered = renderHook(() => useTicketList());
      result = rendered.result;
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      result.current.updateFilters({ status: 'InProgress' });
    });

    // An immediate request must be sent without waiting for debounce or search input
    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'InProgress', page: 1 }),
    );
  });

  it('triggers an immediate request when changing SLA filter without special wiring', async () => {
    let result: { current: ReturnType<typeof useTicketList> };
    await act(async () => {
      const rendered = renderHook(() => useTicketList());
      result = rendered.result;
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      // Simulate Task 2 SLA status filter being updated
      result.current.updateFilters({ slaStatus: 'AtRisk' });
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ slaStatus: 'AtRisk', page: 1 }),
    );
  });

  it('does not send a request per keystroke in search', async () => {
    let result: { current: ReturnType<typeof useTicketList> };
    await act(async () => {
      const rendered = renderHook(() => useTicketList());
      result = rendered.result;
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    // Keystroke 1
    act(() => {
      result.current.updateFilters({ search: 'a' });
    });
    expect(getTickets).toHaveBeenCalledTimes(1);

    // Keystroke 2 after 100ms
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
      result.current.updateFilters({ search: 'ab' });
    });
    expect(getTickets).toHaveBeenCalledTimes(1);

    // Keystroke 3 after another 100ms
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
      result.current.updateFilters({ search: 'abc' });
    });
    expect(getTickets).toHaveBeenCalledTimes(1);

    // Wait full debounce window
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'abc', page: 1 }),
    );
  });

  it('triggers an immediate request when clearing filters via resetFilters', async () => {
    let result: { current: ReturnType<typeof useTicketList> };
    await act(async () => {
      const rendered = renderHook(() => useTicketList());
      result = rendered.result;
    });

    await act(async () => {
      result.current.updateFilters({ status: 'InProgress', priority: 'High' });
    });
    expect(getTickets).toHaveBeenCalledTimes(2);

    await act(async () => {
      result.current.resetFilters();
    });

    expect(getTickets).toHaveBeenCalledTimes(3);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({
        search: '',
        status: undefined,
        priority: undefined,
        page: 1,
      }),
    );
  });
});

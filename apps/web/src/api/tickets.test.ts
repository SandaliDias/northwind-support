import { afterEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from './tickets';
import { emptyFilters } from '../features/tickets/hooks/useTicketList';

afterEach(() => vi.unstubAllGlobals());
describe('ticket query serialization', () => {
  it('sends the SLA filter in the HTTP request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], totalCount: 0 }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await ticketsApi.getTickets({ ...emptyFilters, slaStatus: 'AtRisk', priority: 'High' });
    const url = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost');
    expect(url.pathname).toBe('/api/tickets');
    expect(url.searchParams.get('slaStatus')).toBe('AtRisk');
    expect(url.searchParams.get('priority')).toBe('High');
  });
});

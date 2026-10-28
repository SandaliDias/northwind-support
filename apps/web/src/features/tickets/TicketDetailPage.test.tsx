import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../api/tickets';
import { ticketListItem } from '../../test/fixtures';
import type { TicketDetail, TicketEscalation } from '../../types/api';
import { TicketDetailPage } from './TicketDetailPage';

vi.mock('../../api/tickets', () => ({ ticketsApi: { getTicket: vi.fn(), getEscalations: vi.fn(), escalate: vi.fn() } }));
const reloadReference = vi.hoisted(() => vi.fn());
vi.mock('../../hooks/useReferenceData', () => ({ useReferenceData: () => ({
  data: { agents: [], categories: [], customers: [] }, isLoading: false, reload: reloadReference,
}) }));
const ticket: TicketDetail = { ...ticketListItem(), description: 'Description',
  customer: { id: 2, name: 'Fabrikam', tier: 'Standard', email: 'help@example.com', phone: null },
  category: { id: 2, name: 'Billing', requiresSpecialist: true } };
const entry: TicketEscalation = { id: 1, fromPriority: 'High', toPriority: 'Critical',
  fromAgent: ticket.assignedAgent, toAgent: null, fromDueAtUtc: ticket.dueAtUtc,
  toDueAtUtc: ticket.updatedAtUtc, reason: 'Increased customer impact', escalatedBy: 'Lead',
  escalatedAtUtc: ticket.updatedAtUtc };

async function submit() {
  render(<MemoryRouter initialEntries={['/tickets/12']}><Routes>
    <Route path="/tickets/:id" element={<TicketDetailPage />} />
  </Routes></MemoryRouter>);
  await screen.findByRole('heading', { name: ticket.title });
  fireEvent.change(screen.getByLabelText('Reason'), { target: { value: entry.reason } });
  fireEvent.change(screen.getByLabelText('Escalated by'), { target: { value: 'Lead' } });
  fireEvent.click(screen.getByRole('button', { name: 'Escalate' }));
}

describe('TicketDetailPage escalation', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(ticketsApi.getTicket).mockResolvedValue(ticket);
    vi.mocked(ticketsApi.escalate).mockResolvedValue({ ticket: { ...ticket, priority: 'Critical' }, escalation: entry });
  });

  it('updates the displayed ticket and reloads history without navigation or a full reload', async () => {
    vi.mocked(ticketsApi.getEscalations).mockResolvedValueOnce([]).mockResolvedValue([entry]);
    await submit();
    expect(await screen.findByText(entry.reason)).toBeVisible();
    expect(screen.getByText('Critical', { exact: true })).toBeVisible();
    expect(ticketsApi.getEscalations).toHaveBeenCalledTimes(2);
    expect(ticketsApi.getTicket).toHaveBeenCalledTimes(1);
    expect(reloadReference).toHaveBeenCalled();
  });

  it('keeps the confirmed ticket change when refreshing history fails and offers retry', async () => {
    vi.mocked(ticketsApi.getEscalations).mockResolvedValueOnce([]).mockRejectedValue(new Error('History unavailable'));
    await submit();
    await waitFor(() => expect(screen.getByText(/Unable to load escalation history/)).toBeVisible());
    expect(screen.getByText('Critical', { exact: true })).toBeVisible();
    expect(screen.getByRole('button', { name: /retry|try again/i })).toBeVisible();
  });
});

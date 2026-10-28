import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { ApiError } from '../../../api/client';
import type { EscalateTicketResult, TicketDetail } from '../../../types/api';
import { ticketListItem } from '../../../test/fixtures';
import { EscalationPanel } from './EscalationPanel';

vi.mock('../../../api/tickets', () => ({ ticketsApi: { escalate: vi.fn() } }));
const escalate = vi.mocked(ticketsApi.escalate);
const ticket: TicketDetail = { ...ticketListItem(), description: 'A description for the test ticket.',
  customer: { id: 2, name: 'Fabrikam', tier: 'Standard', email: 'help@example.com', phone: null },
  category: { id: 2, name: 'Billing', requiresSpecialist: true } };

function enter(reason = 'Impact increased') {
  fireEvent.change(screen.getByLabelText('Reason'), { target: { value: reason } });
  fireEvent.change(screen.getByLabelText('Escalated by'), { target: { value: 'Lead' } });
}

describe('EscalationPanel', () => {
  beforeEach(() => vi.resetAllMocks());

  it.each(['four', '    ', 'x'.repeat(501)])('invalid reason prevents the API call', (reason) => {
    render(<EscalationPanel ticket={ticket} onEscalated={vi.fn()} />);
    enter(reason);
    fireEvent.click(screen.getByRole('button', { name: 'Escalate' }));
    expect(screen.getByText('Enter a reason of 5-500 characters.')).toBeVisible();
    expect(escalate).not.toHaveBeenCalled();
  });

  it('a 409 is shown without replacing or mutating the ticket', async () => {
    escalate.mockRejectedValue(new ApiError(409, 'Ticket is already Critical.'));
    const updated = vi.fn();
    const before = structuredClone(ticket);
    render(<EscalationPanel ticket={ticket} onEscalated={updated} />);
    enter();
    fireEvent.click(screen.getByRole('button', { name: 'Escalate' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Ticket is already Critical.');
    expect(updated).not.toHaveBeenCalled();
    expect(ticket).toEqual(before);
    expect(screen.getByLabelText('Reason')).toHaveValue('Impact increased');
  });

  it('disables controls while pending and reports the confirmed response', async () => {
    let finish: ((result: EscalateTicketResult) => void) | undefined;
    escalate.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    const updated = vi.fn();
    render(<EscalationPanel ticket={ticket} onEscalated={updated} />);
    enter('  Impact increased  ');
    fireEvent.click(screen.getByRole('button', { name: 'Escalate' }));
    expect(screen.getByRole('button', { name: 'Escalating...' })).toBeDisabled();
    expect(screen.getByLabelText('Reason')).toBeDisabled();
    expect(escalate).toHaveBeenCalledWith(ticket.id, { reason: 'Impact increased', escalatedBy: 'Lead' });
    const result: EscalateTicketResult = { ticket: { ...ticket, priority: 'Critical' }, escalation: {
      id: 1, fromPriority: 'High', toPriority: 'Critical', fromAgent: null, toAgent: null,
      fromDueAtUtc: null, toDueAtUtc: null, reason: 'Impact increased', escalatedBy: 'Lead', escalatedAtUtc: ticket.createdAtUtc,
    } };
    finish?.(result);
    await waitFor(() => expect(updated).toHaveBeenCalledWith(result));
  });

  it.each([
    { ...ticket, priority: 'Critical' as const },
    { ...ticket, status: 'Resolved' as const },
    { ...ticket, status: 'Closed' as const },
  ])('blocks a ticket that cannot be escalated', (blocked) => {
    render(<EscalationPanel ticket={blocked} onEscalated={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Escalate' })).toBeDisabled();
  });
});

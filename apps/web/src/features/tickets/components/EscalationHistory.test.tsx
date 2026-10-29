import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { TicketEscalation } from '../../../types/api';
import { EscalationHistory } from './EscalationHistory';

afterEach(cleanup);
const entry: TicketEscalation = {
  id: 1, fromPriority: 'Low', toPriority: 'Medium',
  fromAgent: { id: 4, fullName: 'Sara Lindqvist' }, toAgent: { id: 2, fullName: 'Ben Osei' },
  fromDueAtUtc: null, toDueAtUtc: '2026-10-29T12:00:00Z', reason: 'Customer impact increased',
  escalatedBy: 'agent:1', escalatedAtUtc: '2026-10-29T00:00:00Z',
};

describe('EscalationHistory actor', () => {
  it('shows the actor name rather than the audit identifier or assigned agent', () => {
    render(<EscalationHistory entries={[{
      ...entry, escalatedByAgent: { id: 1, fullName: 'Alex Turner' },
    }]} />);
    expect(screen.getByText(/^By Alex Turner/)).toBeVisible();
    expect(screen.queryByText(/^By agent:1/)).not.toBeInTheDocument();
    expect(screen.queryByText(/^By Sara Lindqvist|^By Ben Osei/)).not.toBeInTheDocument();
  });

  it('preserves a legacy actor label when no agent metadata was returned', () => {
    render(<EscalationHistory entries={[{ ...entry, escalatedBy: 'Legacy team lead' }]} />);
    expect(screen.getByText(/^By Legacy team lead/)).toBeVisible();
  });

  it('keeps a missing actor identifiable instead of guessing an owner name', () => {
    render(<EscalationHistory entries={[{ ...entry, escalatedBy: 'agent:999', escalatedByAgent: null }]} />);
    expect(screen.getByText(/^By agent:999/)).toBeVisible();
  });
});

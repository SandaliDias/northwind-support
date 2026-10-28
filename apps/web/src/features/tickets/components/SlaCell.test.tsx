import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaCell } from './SlaCell';

const cases: [SlaStatus, string][] = [
  ['WithinSla', 'Within SLA'], ['AtRisk', 'At risk'], ['Breached', 'Breached'], ['Met', 'Met'], ['NotApplicable', 'No SLA'],
];
describe('SlaCell', () => {
  it.each(cases)('renders %s with distinct text', (status, label) => {
    render(<SlaCell dueAtUtc={null} slaStatus={status} />);
    expect(screen.getByText(label)).toBeVisible();
  });
});

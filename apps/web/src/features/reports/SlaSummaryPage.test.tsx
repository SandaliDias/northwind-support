import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reportsApi } from '../../api/reports';
import { ApiError } from '../../api/client';
import type { SlaSummaryGroup } from '../../types/api';
import { SlaSummaryPage } from './SlaSummaryPage';
import { defaultReportSelection, validateReportSelection } from './reportDates';

vi.mock('../../api/reports', () => ({ reportsApi: { getSlaSummary: vi.fn() } }));
const row: SlaSummaryGroup = { agentId: 1, agentName: 'Alex Turner', priority: null,
  total: 4, breached: 1, atRisk: 1, within: 1, met: 1, notApplicable: 0, breachRate: 25, averageResolutionHours: 3.125 };
beforeEach(() => { vi.resetAllMocks(); vi.mocked(reportsApi.getSlaSummary).mockResolvedValue([row]); });
afterEach(cleanup);
function range(from = '2026-10-01', to = '2026-10-31') {
  fireEvent.change(screen.getByLabelText('From date (UTC)'), { target: { value: from } });
  fireEvent.change(screen.getByLabelText('To date (UTC)'), { target: { value: to } });
}
function generate() { fireEvent.click(screen.getByRole('button', { name: 'Generate report' })); }

describe('SLA summary report', () => {
  it('requests inclusive UTC calendar dates as an exclusive API upper bound and displays every metric', async () => {
    render(<SlaSummaryPage />);
    expect(reportsApi.getSlaSummary).not.toHaveBeenCalled();
    range(); generate();
    const result = await screen.findByRole('row', { name: /Alex Turner/ });
    expect(reportsApi.getSlaSummary).toHaveBeenCalledWith({
      from: '2026-10-01T00:00:00.000Z', to: '2026-11-01T00:00:00.000Z', groupBy: 'agent',
    }, expect.any(AbortSignal));
    expect(within(result).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['4', '1', '1', '1', '1', '0', '25.00%', '3.13']);
    expect(screen.getByText(/Showing tickets created 2026-10-01 through 2026-10-31 UTC/)).toBeVisible();
  });

  it('uses priority grouping and distinguishes a zero average from no resolutions', async () => {
    vi.mocked(reportsApi.getSlaSummary).mockResolvedValue([
      { ...row, agentId: null, agentName: null, priority: 'High', averageResolutionHours: 0 },
      { ...row, agentId: null, agentName: null, priority: 'Low', averageResolutionHours: null },
    ]);
    render(<SlaSummaryPage />); range();
    fireEvent.change(screen.getByLabelText('Group by'), { target: { value: 'priority' } }); generate();
    expect(await screen.findByRole('columnheader', { name: 'Priority' })).toBeVisible();
    expect(within(screen.getByRole('row', { name: /High/ })).getByText('0.00')).toBeVisible();
    expect(within(screen.getByRole('row', { name: /Low/ })).getByText('No resolutions')).toBeVisible();
    expect(vi.mocked(reportsApi.getSlaSummary).mock.calls[0][0].groupBy).toBe('priority');
  });

  it('shows Unassigned and empty results without inventing metrics', async () => {
    vi.mocked(reportsApi.getSlaSummary).mockResolvedValueOnce([{ ...row, agentId: null, agentName: null }]).mockResolvedValue([]);
    render(<SlaSummaryPage />); range(); generate();
    expect(await screen.findByRole('rowheader', { name: 'Unassigned' })).toBeVisible();
    generate();
    expect(await screen.findByText('No tickets in this date range')).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it.each([
    ['', '2026-10-31', 'Choose a valid start date.'],
    ['2026-10-01', '', 'Choose a valid end date.'],
    ['2026-11-01', '2026-10-31', 'End date must be on or after start date.'],
  ])('validates %s to %s before contacting the API', (from, to, message) => {
    render(<SlaSummaryPage />); range(from, to); generate();
    expect(screen.getByText(message)).toBeVisible();
    expect(reportsApi.getSlaSummary).not.toHaveBeenCalled();
  });

  it('disables pending controls, clears an older result, and aborts on unmount', async () => {
    let finish: ((rows: SlaSummaryGroup[]) => void) | undefined;
    vi.mocked(reportsApi.getSlaSummary).mockResolvedValueOnce([row])
      .mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    const view = render(<SlaSummaryPage />); range(); generate();
    await screen.findByRole('row', { name: /Alex Turner/ });
    range('2026-10-02', '2026-10-31'); generate();
    expect(screen.getByRole('button', { name: 'Generating...' })).toBeDisabled();
    expect(screen.getByLabelText('From date (UTC)')).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Loading SLA summary');
    expect(screen.queryByText('Alex Turner')).not.toBeInTheDocument();
    const signal = vi.mocked(reportsApi.getSlaSummary).mock.calls[1][1];
    view.unmount();
    expect(signal?.aborted).toBe(true);
    await act(async () => { finish?.([row]); });
  });

  it('shows server errors and retries the last submitted range even if draft dates changed', async () => {
    vi.mocked(reportsApi.getSlaSummary).mockRejectedValueOnce(new ApiError(503, 'Database unavailable')).mockResolvedValue([row]);
    render(<SlaSummaryPage />); range(); generate();
    expect(await screen.findByRole('alert')).toHaveTextContent('Database unavailable');
    range('2026-10-02', '2026-10-03');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByRole('row', { name: /Alex Turner/ });
    expect(vi.mocked(reportsApi.getSlaSummary).mock.calls[1][0]).toEqual({
      from: '2026-10-01T00:00:00.000Z', to: '2026-11-01T00:00:00.000Z', groupBy: 'agent',
    });
  });

  it('shows field-level API validation details rather than only the generic title', async () => {
    vi.mocked(reportsApi.getSlaSummary).mockRejectedValue(new ApiError(400, 'Validation failed', {
      errors: { From: ['Start date is outside the supported range.'] },
    }));
    render(<SlaSummaryPage />); range(); generate();
    expect(await screen.findByRole('alert')).toHaveTextContent('Start date is outside the supported range.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Generate report' })).toBeEnabled());
  });
});

describe('report calendar dates', () => {
  it('defaults to the UTC month and day rather than the browser offset', () => {
    expect(defaultReportSelection(new Date('2026-11-01T01:00:00+05:30')))
      .toEqual({ from: '2026-10-01', to: '2026-10-31', groupBy: 'agent' });
  });
  it('supports a single day and leap day, and rejects impossible dates or an overflowing upper bound', () => {
    expect(validateReportSelection({ from: '2028-02-29', to: '2028-02-29', groupBy: 'agent' }).query)
      .toEqual({ from: '2028-02-29T00:00:00.000Z', to: '2028-03-01T00:00:00.000Z', groupBy: 'agent' });
    expect(validateReportSelection({ from: '2026-02-29', to: '2026-03-01', groupBy: 'agent' }).errors?.from).toBeTruthy();
    expect(validateReportSelection({ from: '9999-12-30', to: '9999-12-31', groupBy: 'agent' }).errors?.to).toBeTruthy();
  });
});

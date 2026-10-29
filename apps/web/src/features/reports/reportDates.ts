import type { SlaSummaryGrouping, SlaSummaryQuery } from '../../types/api';

export interface ReportSelection { from: string; to: string; groupBy: SlaSummaryGrouping }
export type ReportErrors = Partial<Record<keyof ReportSelection, string>>;

export function defaultReportSelection(now = new Date()): ReportSelection {
  const today = now.toISOString().slice(0, 10);
  return { from: `${today.slice(0, 8)}01`, to: today, groupBy: 'agent' };
}

function utcDate(value: string): Date | undefined {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return undefined;
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value ? date : undefined;
}

/** Calendar dates are UTC and inclusive in the UI; the API receives a half-open interval. */
export function validateReportSelection(selection: ReportSelection):
  { query: SlaSummaryQuery; errors?: never } | { query?: never; errors: ReportErrors } {
  const errors: ReportErrors = {};
  const from = utcDate(selection.from);
  const to = utcDate(selection.to);
  if (!from) errors.from = 'Choose a valid start date.';
  if (!to) errors.to = 'Choose a valid end date.';
  if (from && to && from > to) errors.to = 'End date must be on or after start date.';
  const exclusiveEnd = to ? new Date(to.getTime() + 86400000) : undefined;
  if (exclusiveEnd && exclusiveEnd.getUTCFullYear() > 9999) errors.to = 'Choose an end date before 9999-12-31.';
  if (Object.keys(errors).length || !from || !exclusiveEnd) return { errors };
  return { query: { from: from.toISOString(), to: exclusiveEnd.toISOString(), groupBy: selection.groupBy } };
}

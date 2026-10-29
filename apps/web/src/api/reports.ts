import { request } from './client';
import type { SlaSummaryGroup, SlaSummaryQuery } from '../types/api';

export const reportsApi = {
  getSlaSummary: (query: SlaSummaryQuery, signal?: AbortSignal) =>
    request<SlaSummaryGroup[]>(`/reports/sla-summary?${new URLSearchParams({ ...query })}`, { signal }),
};

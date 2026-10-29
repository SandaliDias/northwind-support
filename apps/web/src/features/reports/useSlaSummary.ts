import { useEffect, useState } from 'react';
import { ApiError, toErrorMessage } from '../../api/client';
import { reportsApi } from '../../api/reports';
import type { SlaSummaryGroup, SlaSummaryQuery } from '../../types/api';

interface ReportState {
  query?: SlaSummaryQuery;
  revision: number;
  data?: SlaSummaryGroup[];
  error?: string;
}

export function useSlaSummary(query?: SlaSummaryQuery) {
  const [state, setState] = useState<ReportState>({ revision: 0 });
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    if (!query) return;
    const controller = new AbortController();
    void reportsApi.getSlaSummary(query, controller.signal).then((data) => {
      if (!controller.signal.aborted) setState({ query, revision: retry, data });
    }).catch((error: unknown) => {
      if (controller.signal.aborted) return;
      const details = error instanceof ApiError ? Object.values(error.fieldErrors).flat().join(' ') : '';
      setState({ query, revision: retry, error: details || toErrorMessage(error, 'Unable to load SLA summary.') });
    });
    return () => controller.abort();
  }, [query, retry]);
  // Do not briefly display the previous report under a newly applied range/group heading.
  const settled = state.query === query && state.revision === retry;
  return { data: settled ? state.data : undefined, error: settled ? state.error : undefined,
    isLoading: Boolean(query) && !settled, reload: () => setRetry((value) => value + 1) };
}

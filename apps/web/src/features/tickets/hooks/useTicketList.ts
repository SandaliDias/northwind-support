import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  isLoading: boolean;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const [state, setState] = useState<State>({ isLoading: true });

  const prevFiltersRef = useRef<TicketFilters | undefined>(undefined);
  const isResetRef = useRef(false);
  const requestIdRef = useRef(0);

  const load = useCallback(async (queryFilters?: TicketFilters) => {
    const targetFilters = queryFilters ?? filters;
    const requestId = ++requestIdRef.current;
    setState((current) => ({ ...current, isLoading: true, error: undefined }));

    try {
      const data = await ticketsApi.getTickets(targetFilters);
      if (requestId === requestIdRef.current) {
        setState({ isLoading: false, data, error: undefined });
      }
    } catch (error) {
      if (requestId === requestIdRef.current) {
        const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
        setState((current) => ({ ...current, isLoading: false, error: message }));
      }
    }
  }, [filters]);

  useEffect(() => {
    const prev = prevFiltersRef.current;
    prevFiltersRef.current = filters;

    // Initial mount: load immediately
    if (!prev) {
      void load(filters);
      return;
    }

    // Explicit reset: load immediately
    if (isResetRef.current) {
      isResetRef.current = false;
      void load(filters);
      return;
    }

    const { search: prevSearch, ...prevOthers } = prev;
    const { search: currSearch, ...currOthers } = filters;

    const searchChanged = prevSearch !== currSearch;
    // When typing in search, page may reset to 1 in the same update.
    // If only search (and the resulting page reset) changed, debounce.
    const keysToCheck = Object.keys({ ...prevOthers, ...currOthers }).filter(
      (key) => !searchChanged || key !== 'page',
    );

    const hasOtherChanges = keysToCheck.some(
      (key) =>
        prevOthers[key as keyof typeof prevOthers] !==
        currOthers[key as keyof typeof currOthers],
    );

    if (hasOtherChanges) {
      // Immediate request for any filter change
      void load(filters);
      return;
    }

    if (searchChanged) {
      // Debounce keystrokes in search
      const handle = window.setTimeout(() => {
        void load(filters);
      }, DEBOUNCE_MS);

      return () => window.clearTimeout(handle);
    }
  }, [filters, load]);

  const updateFilters = (patch: Partial<TicketFilters>) =>
    setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 }));

  const resetFilters = () => {
    isResetRef.current = true;
    setFilters(emptyFilters);
  };

  return {
    filters,
    updateFilters,
    resetFilters,
    reload: () => void load(),
    ...state,
  };
}

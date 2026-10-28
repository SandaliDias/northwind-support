import { useCallback } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { useAsyncData } from '../../../hooks/useAsyncData';

export function useEscalations(ticketId: number) {
  const load = useCallback(() => ticketsApi.getEscalations(ticketId), [ticketId]);
  return useAsyncData(load, 'Unable to load escalation history.');
}

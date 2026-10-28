import { useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalateTicketResult, TicketDetail } from '../../../types/api';

interface Props { ticket: TicketDetail; onEscalated: (result: EscalateTicketResult) => void }

export function EscalationPanel({ ticket, onEscalated }: Props) {
  const [reason, setReason] = useState('');
  const [actor, setActor] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);
  const blocked = ticket.priority === 'Critical' || ticket.status === 'Resolved' || ticket.status === 'Closed';

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending || blocked) return;
    const validation: Record<string, string> = {};
    if (reason.trim().length < 5 || reason.trim().length > 500) validation.reason = 'Enter a reason of 5-500 characters.';
    if (actor.trim().length < 1 || actor.trim().length > 100) validation.escalatedBy = 'Enter an actor of 1-100 characters.';
    setErrors(validation);
    setError(undefined);
    if (Object.keys(validation).length > 0) return;
    setPending(true);
    try {
      const result = await ticketsApi.escalate(ticket.id, { reason: reason.trim(), escalatedBy: actor.trim() });
      setReason('');
      onEscalated(result);
    } catch (caught) {
      if (caught instanceof ApiError) {
        setErrors(Object.fromEntries(Object.entries(caught.fieldErrors).map(([key, value]) =>
          [key.charAt(0).toLowerCase() + key.slice(1), value[0]])));
      }
      setError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally { setPending(false); }
  };

  return <form className="card form" onSubmit={(event) => void submit(event)} noValidate aria-label="Escalate ticket">
    <h2>Escalate ticket</h2>
    {blocked && <p>This ticket cannot be escalated because it is {ticket.priority === 'Critical' ? 'Critical' : ticket.status}.</p>}
    <Field id="escalation-reason" label="Reason" error={errors.reason} hint="5-500 characters after trimming.">
      {(props) => <textarea {...props} value={reason} onChange={(event) => setReason(event.target.value)}
        rows={3} disabled={pending || blocked} />}
    </Field>
    <Field id="escalation-actor" label="Escalated by" error={errors.escalatedBy}
      hint="Temporary actor field until agent sign-in is implemented.">
      {(props) => <input {...props} value={actor} onChange={(event) => setActor(event.target.value)}
        disabled={pending || blocked} />}
    </Field>
    {error && <p role="alert" className="field__error">{error}</p>}
    <button type="submit" className="button button--primary" disabled={pending || blocked}>
      {pending ? 'Escalating...' : 'Escalate'}
    </button>
  </form>;
}

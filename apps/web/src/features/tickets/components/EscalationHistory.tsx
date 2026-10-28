import type { TicketEscalation } from '../../../types/api';
import { formatDateTime } from '../../../lib/format';

export function EscalationHistory({ entries }: { entries: TicketEscalation[] }) {
  return <div className="card">
    <h2>Escalation history</h2>
    {entries.length === 0 ? <p>No escalations yet.</p> : <ol>
      {entries.map((entry) => <li key={entry.id}>
        <p><strong>{entry.fromPriority} → {entry.toPriority}</strong></p>
        <p>Agent: {entry.fromAgent?.fullName ?? 'Unassigned'} → {entry.toAgent?.fullName ?? 'Unassigned'}</p>
        <p>Due: {formatDateTime(entry.fromDueAtUtc)} → {formatDateTime(entry.toDueAtUtc)}</p>
        <p>{entry.reason}</p>
        <p>By {entry.escalatedBy} · {formatDateTime(entry.escalatedAtUtc)}</p>
      </li>)}
    </ol>}
  </div>;
}

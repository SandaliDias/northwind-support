using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

public sealed class EscalationStore(SupportDbContext db) : IEscalationStore
{
    public async Task AddAsync(TicketEscalation escalation, CancellationToken ct) =>
        await db.Set<TicketEscalation>().AddAsync(escalation, ct);

    public async Task<IReadOnlyList<TicketEscalationDto>> GetForTicketAsync(int ticketId, CancellationToken ct) =>
        await (from e in db.Set<TicketEscalation>().AsNoTracking()
               from before in db.Set<Agent>().Where(a => a.Id == e.FromAgentId).DefaultIfEmpty()
               from after in db.Set<Agent>().Where(a => a.Id == e.ToAgentId).DefaultIfEmpty()
               where e.TicketId == ticketId
               orderby e.EscalatedAtUtc descending, e.Id descending
               select new TicketEscalationDto(e.Id, e.FromPriority, e.ToPriority,
                   before == null ? null : new AgentSummaryDto(before.Id, before.FullName),
                   after == null ? null : new AgentSummaryDto(after.Id, after.FullName),
                   e.FromDueAtUtc, e.ToDueAtUtc, e.Reason, e.EscalatedBy, e.EscalatedAtUtc))
            .ToListAsync(ct);
}

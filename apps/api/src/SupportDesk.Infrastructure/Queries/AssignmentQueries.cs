using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

public sealed class AssignmentQueries(SupportDbContext db) : IAssignmentQueries
{
    public async Task<IReadOnlyList<AssignmentCandidate>> GetCandidatesAsync(int categoryId, CancellationToken ct) =>
        await db.Set<Agent>().AsNoTracking().Select(a => new AssignmentCandidate(
            a.Id, a.FullName, a.IsActive, a.MaxOpenTickets,
            db.Set<Ticket>().Count(t => t.AssignedAgentId == a.Id
                && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
            a.Specializations.Any(s => s.CategoryId == categoryId))).ToListAsync(ct);
}

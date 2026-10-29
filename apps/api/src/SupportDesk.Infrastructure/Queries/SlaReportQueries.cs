using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

public sealed class SlaReportQueries(SupportDbContext db, IClock clock, SlaPolicy policy) : ISlaReportQueries
{
    public async Task<IReadOnlyList<SlaSummaryGroupDto>> GetSummaryAsync(SlaSummaryRange range, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var threshold = policy.AtRiskThresholdPercent;
        var byAgent = range.GroupBy == SlaSummaryGrouping.Agent;
        var cohort =
            from ticket in db.Set<Ticket>().AsNoTracking()
            where ticket.CreatedAtUtc >= range.FromUtc && ticket.CreatedAtUtc < range.ToUtc
            from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
            select new
            {
                AgentId = byAgent ? ticket.AssignedAgentId : null,
                AgentName = byAgent ? (agent == null ? "Unassigned" : agent.FullName) : null,
                Priority = byAgent ? (TicketPriority?)null : ticket.Priority,
                Status = (SlaStatus)SupportDbContext.TicketSlaStatus(ticket.DueAtUtc, ticket.ResolvedAtUtc,
                    ticket.SlaStartedAtUtc ?? ticket.CreatedAtUtc, now, threshold),
                ticket.CreatedAtUtc,
                ticket.ResolvedAtUtc
            };

        // Nothing is materialized until this grouped projection. Counts, rates and averages
        // all execute in the same SQL command, including the shared SLA function.
        return await cohort.GroupBy(x => new { x.AgentId, x.AgentName, x.Priority })
            .Select(group => new SlaSummaryGroupDto
            {
                AgentId = group.Key.AgentId,
                AgentName = group.Key.AgentName,
                Priority = group.Key.Priority,
                Total = group.Count(),
                Breached = group.Count(x => x.Status == SlaStatus.Breached),
                AtRisk = group.Count(x => x.Status == SlaStatus.AtRisk),
                Within = group.Count(x => x.Status == SlaStatus.WithinSla),
                Met = group.Count(x => x.Status == SlaStatus.Met),
                NotApplicable = group.Count(x => x.Status == SlaStatus.NotApplicable),
                BreachRate = Math.Round(group.Count(x => x.Status == SlaStatus.Breached) * 100.0 / group.Count(), 2),
                // Split whole days and time of day: DATEDIFF(millisecond) across a whole
                // long-running ticket would overflow after approximately 24.8 days.
                AverageResolutionHours = group.Average(x => x.ResolvedAtUtc == null ? (double?)null :
                    EF.Functions.DateDiffDay(x.CreatedAtUtc, x.ResolvedAtUtc.Value) * 24.0 +
                    (EF.Functions.DateDiffMillisecond(x.ResolvedAtUtc.Value.Date, x.ResolvedAtUtc.Value) -
                     EF.Functions.DateDiffMillisecond(x.CreatedAtUtc.Date, x.CreatedAtUtc)) / 3600000.0)
            })
            .OrderBy(x => x.AgentId).ThenBy(x => x.Priority)
            .ToListAsync(ct);
    }
}

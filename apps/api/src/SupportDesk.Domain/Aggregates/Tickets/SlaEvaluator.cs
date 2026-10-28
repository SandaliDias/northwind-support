namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date. SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
public static class SlaEvaluator
{
    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc,
        DateTime? startedAtUtc, DateTime nowUtc, decimal thresholdPercent) =>
        dueAtUtc is null ? SlaStatus.NotApplicable
        : resolvedAtUtc is not null
            ? (resolvedAtUtc <= dueAtUtc ? SlaStatus.Met : SlaStatus.Breached)
        : nowUtc > dueAtUtc ? SlaStatus.Breached
        : startedAtUtc is not null
            && (decimal)(dueAtUtc.Value - nowUtc).Ticks * 100
                <= (decimal)(dueAtUtc.Value - startedAtUtc.Value).Ticks * thresholdPercent
            ? SlaStatus.AtRisk
        : SlaStatus.WithinSla;

    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc, DateTime nowUtc) =>
        Evaluate(dueAtUtc, resolvedAtUtc, null, nowUtc, 25);
}

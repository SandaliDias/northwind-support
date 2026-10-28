using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets;

public sealed record TriageDecision(TicketPriority Priority, DateTime DueAtUtc,
    int? AgentId, TriageDto Explanation);

public sealed class TicketTriageService(IAssignmentQueries assignments, SlaPolicy sla)
{
    public async Task<TriageDecision> DecideAsync(Category category, Customer customer,
        TicketPriority? requested, DateTime now, CancellationToken ct,
        int? preferredAgentId = null, bool escalation = false)
    {
        var priority = escalation ? requested!.Value : SlaPolicy.Priority(requested, category.ForcesCriticalPriority);
        var window = sla.Window(priority, customer.Tier);
        var candidates = await assignments.GetCandidatesAsync(category.Id, ct);
        var selected = AgentAssignmentPolicy.Select(candidates, category.RequiresSpecialist, preferredAgentId);
        var priorityReason = escalation ? $"Escalated one level to {priority}."
            : category.ForcesCriticalPriority ? $"Category policy forces Critical (requested {requested?.ToString() ?? "default Medium"})."
            : requested is null ? "No priority requested; defaulted to Medium." : $"Requested priority {priority} retained.";
        var assignmentReason = selected is null
            ? "Unassigned because no active agent meets the specialization and open-ticket capacity requirements."
            : selected.Id == preferredAgentId ? $"Kept {selected.FullName}; the current owner remains eligible."
            : $"Assigned {selected.FullName}: {selected.OpenTicketCount} open tickets, the lowest load among eligible agents.";
        var windowReason = customer.Tier == CustomerTier.Premium
            ? $"Premium SLA: {window.TotalHours:0.###} hours (base {sla.BaseWindowHours[priority]:0.###} hours × {sla.PremiumCustomerMultiplier:0.###}, minimum {sla.MinimumWindowHours:0.###} hours)."
            : $"Standard SLA: configured {priority} window of {window.TotalHours:0.###} hours.";
        return new(priority, now.Add(window), selected?.Id,
            new(priorityReason, $"{windowReason} Measured from {(escalation ? "escalation" : "creation")}.", assignmentReason));
    }
}

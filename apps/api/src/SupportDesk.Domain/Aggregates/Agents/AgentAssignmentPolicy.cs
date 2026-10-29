namespace SupportDesk.Domain.Aggregates.Agents;

public sealed record AssignmentCandidate(int Id, string FullName, bool IsActive,
    int MaxOpenTickets, int OpenTicketCount, bool IsSpecialist);

/// <summary>The same eligibility decision is used by create, escalation and manual assignment.</summary>
public static class AgentAssignmentPolicy
{
    public static bool IsEligible(AssignmentCandidate agent, bool requiresSpecialist) =>
        agent.IsActive && agent.OpenTicketCount < agent.MaxOpenTickets
        && (!requiresSpecialist || agent.IsSpecialist);

    public static AssignmentCandidate? Select(IEnumerable<AssignmentCandidate> candidates,
        bool requiresSpecialist, int? preferredAgentId = null)
    {
        var eligible = candidates.Where(a => IsEligible(a, requiresSpecialist)).ToList();
        return eligible.FirstOrDefault(a => a.Id == preferredAgentId)
            ?? eligible.OrderBy(a => a.OpenTicketCount).ThenBy(a => a.Id).FirstOrDefault();
    }
}

using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

public interface IAssignmentQueries
{
    Task<IReadOnlyList<AssignmentCandidate>> GetCandidatesAsync(int categoryId, CancellationToken ct);
}

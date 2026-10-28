using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads <see cref="Agent"/> aggregates.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);

    Task<Agent?> GetByIdAsync(int id, CancellationToken ct);
}

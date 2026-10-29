using SupportDesk.Application.Contracts.Agents;
using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// The read side of agents.
/// </summary>
public interface IAgentQueries
{
    Task<AuthUserDto?> GetActiveUserAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<AgentDto>> GetAllAsync(CancellationToken ct);
}

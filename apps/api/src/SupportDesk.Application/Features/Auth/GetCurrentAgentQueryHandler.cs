using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
namespace SupportDesk.Application.Features.Auth;

public sealed class GetCurrentAgentQueryHandler(IAgentQueries agents, ICurrentAgent current)
{
    public async Task<AuthUserDto> HandleAsync(CancellationToken ct)
    {
        var agent = await agents.GetActiveUserAsync(current.AgentId, ct);
        if (agent is null) throw new UnauthenticatedException();
        return agent;
    }
}

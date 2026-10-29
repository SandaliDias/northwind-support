using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
namespace SupportDesk.Application.Abstractions;

public interface ITokenIssuer
{
    LoginResponse Issue(Agent agent);
}

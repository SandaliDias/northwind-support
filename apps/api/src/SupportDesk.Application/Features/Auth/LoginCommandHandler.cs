using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
namespace SupportDesk.Application.Features.Auth;

public sealed class LoginCommandHandler(IAgentRepository agents, IPasswordService passwords,
    ITokenIssuer tokens, IUnitOfWork unitOfWork)
{
    public const string InvalidCredentials = "Invalid email or password.";

    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var validation = await new LoginRequestValidator().ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        var agent = await agents.GetByEmailAsync(Agent.NormalizeEmail(request.Email), ct);
        // Unknown/inactive/missing-credential accounts still perform a vetted password verification.
        var check = passwords.Verify(agent, request.Password);
        if (agent is null || !agent.IsActive || check == PasswordCheck.Failed)
        {
            throw new UnauthenticatedException(InvalidCredentials);
        }

        if (check == PasswordCheck.RehashNeeded)
        {
            agent.SetPasswordHash(passwords.Hash(agent, request.Password));
            await unitOfWork.SaveChangesAsync(ct);
        }
        return tokens.Issue(agent);
    }
}

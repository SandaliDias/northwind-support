using Moq;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.UnitTests.TestDoubles;
namespace SupportDesk.UnitTests.Application.Features.Auth;

public sealed class LoginCommandHandlerTests
{
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IPasswordService> _passwords = new();
    private readonly Mock<ITokenIssuer> _tokens = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private LoginCommandHandler Handler() => new(_agents.Object, _passwords.Object, _tokens.Object, _unitOfWork.Object);
    private Agent Account(bool active = true)
    {
        var agent = new Agent("Alex Turner", "alex@example.com", 10, FixedClock.DefaultNow);
        if (!active) agent.Deactivate();
        _agents.Setup(a => a.GetByEmailAsync("alex@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        return agent;
    }

    [Theory]
    [InlineData(false, true, PasswordCheck.Failed)]
    [InlineData(true, true, PasswordCheck.Failed)]
    [InlineData(true, false, PasswordCheck.Success)]
    public async Task AllCredentialFailuresAreIdenticalAndIssueNoToken(bool exists, bool active, PasswordCheck check)
    {
        var agent = exists ? Account(active) : null;
        _passwords.Setup(p => p.Verify(agent, "wrong password")).Returns(check);
        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(() => Handler().HandleAsync(new("alex@example.com", "wrong password"), default));
        Assert.Equal("Invalid email or password.", exception.Message);
        _passwords.Verify(p => p.Verify(agent, "wrong password"), Times.Once);
        _tokens.Verify(t => t.Issue(It.IsAny<Agent>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(PasswordCheck.Success, 0)]
    [InlineData(PasswordCheck.RehashNeeded, 1)]
    public async Task SuccessfulLoginNormalizesOnlyEmailAndRehashesWhenRequired(PasswordCheck check, int saves)
    {
        var agent = Account();
        const string password = "  Password with significant spaces  ";
        _passwords.Setup(p => p.Verify(agent, password)).Returns(check);
        _passwords.Setup(p => p.Hash(agent, password)).Returns("upgraded-vetted-hash");
        var response = new LoginResponse("token", FixedClock.DefaultNow.AddHours(1), new(1, agent.FullName, agent.Email));
        _tokens.Setup(t => t.Issue(agent)).Returns(response);
        Assert.Equal(response, await Handler().HandleAsync(new("  ALEX@EXAMPLE.COM  ", password), default));
        _passwords.Verify(p => p.Verify(agent, password), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(saves));
        if (saves > 0) Assert.Equal("upgraded-vetted-hash", agent.PasswordHash);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("invalid-email", "secret")]
    [InlineData("alex@example.com", "")]
    public async Task MalformedInputDoesNotQueryCredentials(string email, string password)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Handler().HandleAsync(new(email, password), default));
        _agents.Verify(a => a.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

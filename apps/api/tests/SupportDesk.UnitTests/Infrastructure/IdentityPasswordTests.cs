using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Services;
using SupportDesk.UnitTests.TestDoubles;
namespace SupportDesk.UnitTests.Infrastructure;

public sealed class IdentityPasswordTests
{
    [Fact]
    public void HashesAreSaltedAndVerifyExactPasswordOnly()
    {
        var passwords = new IdentityPasswordService();
        var agent = new Agent("Alex", "alex@example.com", 10, FixedClock.DefaultNow);
        const string password = "  Local password 1!  ";
        var first = passwords.Hash(agent, password);
        var second = passwords.Hash(agent, password);
        Assert.NotEqual(password, first);
        Assert.NotEqual(first, second);
        agent.SetPasswordHash(first);
        Assert.Equal(PasswordCheck.Success, passwords.Verify(agent, password));
        Assert.Equal(PasswordCheck.Failed, passwords.Verify(agent, "incorrect"));
        Assert.Equal(PasswordCheck.Failed, passwords.Verify(agent, password.Trim()));
        agent.SetPasswordHash(second);
        Assert.Equal(PasswordCheck.Success, passwords.Verify(agent, password));
    }

    [Fact]
    public void UnknownMissingAndMalformedCredentialsCannotVerify()
    {
        var passwords = new IdentityPasswordService();
        var agent = new Agent("Alex", "alex@example.com", 10, FixedClock.DefaultNow);
        Assert.Equal(PasswordCheck.Failed, passwords.Verify(null, "a password"));
        Assert.Equal(PasswordCheck.Failed, passwords.Verify(agent, "a password"));
        agent.SetPasswordHash("not-a-valid-encoded-hash");
        Assert.Equal(PasswordCheck.Failed, passwords.Verify(agent, "a password"));
    }
}

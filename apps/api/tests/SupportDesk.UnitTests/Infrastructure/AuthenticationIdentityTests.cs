using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Presentation.Authentication;
using SupportDesk.UnitTests.TestDoubles;
namespace SupportDesk.UnitTests.Infrastructure;

public sealed class AuthenticationIdentityTests
{
    [Fact]
    public void MissingOrInvalidIdentityCannotSupplyAnActor()
    {
        var context = new DefaultHttpContext();
        var current = new CurrentAgent(new HttpContextAccessor { HttpContext = context });
        Assert.Throws<UnauthenticatedException>(() => current.AgentId);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "0") }, "Bearer"));
        Assert.Throws<UnauthenticatedException>(() => current.AuditActor);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "4"), new Claim("name", new string('x', 200)) }, "Bearer"));
        Assert.Equal(4, current.AgentId);
        Assert.Equal("agent:4", current.AuditActor);
    }

    [Fact]
    public void TokenExpiryUsesTheConfiguredLifetimeAndInjectedClock()
    {
        var options = new JwtOptions { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), TokenLifetimeMinutes = 60 };
        options.Validate();
        var clock = new FixedClock();
        clock.Advance(TimeSpan.FromMilliseconds(123));
        var result = new JwtTokenIssuer(options, clock).Issue(new Agent("Alex", "alex@example.com", 10, clock.UtcNow));
        Assert.Equal(FixedClock.DefaultNow.AddMinutes(60), result.ExpiresAtUtc);
        Assert.Equal(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(result.Token).ValidTo, result.ExpiresAtUtc);
        Assert.Equal("Alex", result.User.FullName);
        Assert.NotEmpty(result.Token);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(10, 60)]
    [InlineData(48, 0)]
    [InlineData(48, 1441)]
    public void MissingWeakKeyOrInvalidLifetimeFailsStartup(int bytes, int minutes) =>
        Assert.Throws<InvalidOperationException>(() => new JwtOptions
        { SigningKey = new string('x', bytes), TokenLifetimeMinutes = minutes }.Validate());
}

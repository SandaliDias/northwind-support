using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
namespace SupportDesk.Presentation.Authentication;

public sealed class JwtTokenIssuer(JwtOptions options, IClock clock) : ITokenIssuer
{
    public LoginResponse Issue(Agent agent)
    {
        var now = clock.UtcNow;
        var expiry = now.AddMinutes(options.TokenLifetimeMinutes);
        var token = new JwtSecurityToken(options.Issuer, options.Audience,
            new[] {
                new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim("name", agent.FullName), new Claim(JwtRegisteredClaimNames.Email, agent.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
            }, now, expiry, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo, new(agent.Id, agent.FullName, agent.Email));
    }
}

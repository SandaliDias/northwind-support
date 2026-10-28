using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
namespace SupportDesk.Presentation.Authentication;

public static class AuthenticationConfiguration
{
    public static IServiceCollection AddAgentAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new();
        jwt.Validate();
        services.AddSingleton(jwt);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAgent, CurrentAgent>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                NameClaimType = "name",
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!int.TryParse(context.Principal?.FindFirst("sub")?.Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var id) || id <= 0)
                    {
                        context.Fail("Invalid agent identity.");
                        return;
                    }
                    var agent = await context.HttpContext.RequestServices.GetRequiredService<IAgentQueries>()
                        .GetActiveUserAsync(id, context.HttpContext.RequestAborted);
                    if (agent is null) context.Fail("Invalid agent identity.");
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Title = "Unauthorized",
                        Status = 401,
                        Detail = "Authentication is required.",
                        Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier }
                    }, context.HttpContext.RequestAborted);
                }
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        return services;
    }
}

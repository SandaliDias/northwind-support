using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Features.Auth;
namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, [FromServices] LoginCommandHandler handler, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await handler.HandleAsync(request, ct);
    }

    [HttpGet("me")]
    public Task<AuthUserDto> Me([FromServices] GetCurrentAgentQueryHandler handler, CancellationToken ct) => handler.HandleAsync(ct);
}

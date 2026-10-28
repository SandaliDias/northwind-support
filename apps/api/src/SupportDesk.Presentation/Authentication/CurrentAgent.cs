using System.Globalization;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Exceptions;
namespace SupportDesk.Presentation.Authentication;

public sealed class CurrentAgent(IHttpContextAccessor accessor) : ICurrentAgent
{
    public int AgentId => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
        && int.TryParse(accessor.HttpContext.User.FindFirst("sub")?.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id : throw new UnauthenticatedException();
    public string AuditActor => $"agent:{AgentId.ToString(CultureInfo.InvariantCulture)}";
}

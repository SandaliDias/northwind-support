using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Application.Features.Reports.GetSlaSummary;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>Aggregates current SLA outcomes for tickets created in [from, to), grouped by current agent or priority.</summary>
    [HttpGet("sla-summary")]
    public Task<IReadOnlyList<SlaSummaryGroupDto>> SlaSummary([FromQuery] SlaSummaryQuery query,
        [FromServices] GetSlaSummaryQueryHandler handler, CancellationToken ct) => handler.HandleAsync(query, ct);
}

using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Application.Features.Reports.GetSlaSummary;

public sealed class GetSlaSummaryQueryHandler(ISlaReportQueries reports)
{
    public async Task<IReadOnlyList<SlaSummaryGroupDto>> HandleAsync(SlaSummaryQuery query, CancellationToken ct)
    {
        var validation = await new SlaSummaryQueryValidator().ValidateAsync(query, ct);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }
        SlaSummaryQuery.TryUtc(query.From, out var from);
        SlaSummaryQuery.TryUtc(query.To, out var to);
        var grouping = string.Equals(query.GroupBy, "agent", StringComparison.OrdinalIgnoreCase)
            ? SlaSummaryGrouping.Agent : SlaSummaryGrouping.Priority;
        return await reports.GetSummaryAsync(new SlaSummaryRange(from, to, grouping), ct);
    }
}

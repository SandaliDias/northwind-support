using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Abstractions;

public interface ISlaReportQueries
{
    Task<IReadOnlyList<SlaSummaryGroupDto>> GetSummaryAsync(SlaSummaryRange range, CancellationToken ct);
}

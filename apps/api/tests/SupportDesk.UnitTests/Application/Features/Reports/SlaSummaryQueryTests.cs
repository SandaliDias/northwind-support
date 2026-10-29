using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Application.Features.Reports.GetSlaSummary;
using ApplicationValidationException = SupportDesk.Application.Exceptions.ValidationException;

namespace SupportDesk.UnitTests.Application.Features.Reports;

public sealed class SlaSummaryQueryTests
{
    private static readonly SlaSummaryQuery Valid = new()
    {
        From = "2026-10-01T00:00:00Z",
        To = "2026-11-01T00:00:00Z"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10-01")]
    [InlineData("2026-10-01T00:00:00")]
    [InlineData("2026-02-30T00:00:00Z")]
    [InlineData("2026-10-01T00:00:00+25:00")]
    [InlineData("not a date")]
    public async Task MissingMalformedOrOffsetlessBoundsNeverReachTheDatabase(string? value)
    {
        var reports = new Mock<ISlaReportQueries>(MockBehavior.Strict);
        var handler = new GetSlaSummaryQueryHandler(reports.Object);
        foreach (var request in new[] { Valid with { From = value }, Valid with { To = value } })
        {
            await Assert.ThrowsAsync<ApplicationValidationException>(() => handler.HandleAsync(request, default));
        }
        reports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("customer")]
    [InlineData("0")]
    [InlineData(" agent ")]
    public async Task UnsupportedGroupingNeverReachesTheDatabase(string? grouping)
    {
        var reports = new Mock<ISlaReportQueries>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            new GetSlaSummaryQueryHandler(reports.Object).HandleAsync(Valid with { GroupBy = grouping! }, default));
        reports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("2026-10-01T00:00:00Z")]
    [InlineData("2026-09-30T23:59:59Z")]
    [InlineData("2026-10-01T05:30:00+05:30")]
    public void EqualOrReversedInstantsAreRejected(string to)
    {
        var result = new SlaSummaryQueryValidator().Validate(Valid with { To = to });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SlaSummaryQuery.To));
    }

    [Theory]
    [InlineData("agent", SlaSummaryGrouping.Agent)]
    [InlineData("PRIORITY", SlaSummaryGrouping.Priority)]
    public async Task OffsetsNormalizeToUtcAndCancellationIsForwarded(string groupBy, SlaSummaryGrouping grouping)
    {
        var reports = new Mock<ISlaReportQueries>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<SlaSummaryGroupDto> rows = [new() { Total = 3 }];
        reports.Setup(r => r.GetSummaryAsync(It.Is<SlaSummaryRange>(range =>
                range.FromUtc == new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) &&
                range.ToUtc == new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) &&
                range.FromUtc.Kind == DateTimeKind.Utc && range.ToUtc.Kind == DateTimeKind.Utc &&
                range.GroupBy == grouping), cancellation.Token)).ReturnsAsync(rows);
        var actual = await new GetSlaSummaryQueryHandler(reports.Object).HandleAsync(Valid with
        {
            From = "2026-10-01T05:30:00+05:30",
            To = "2026-10-31T20:00:00-04:00",
            GroupBy = groupBy
        }, cancellation.Token);
        Assert.Same(rows, actual);
        reports.VerifyAll();
    }

    [Fact]
    public void FractionalSecondsAndDefaultGroupingAreAccepted()
    {
        var query = Valid with { From = "2026-10-01T00:00:00.1234567Z" };
        Assert.Equal("agent", query.GroupBy);
        Assert.True(new SlaSummaryQueryValidator().Validate(query).IsValid);
        Assert.True(SlaSummaryQuery.TryUtc(query.From, out var timestamp));
        Assert.Equal(1234567, timestamp.Ticks % TimeSpan.TicksPerSecond);
    }
}

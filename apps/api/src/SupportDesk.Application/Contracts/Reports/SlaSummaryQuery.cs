using System.Globalization;
using System.Text.RegularExpressions;

namespace SupportDesk.Application.Contracts.Reports;

/// <summary>A creation-time cohort, including From and excluding To. Both require an explicit UTC offset.</summary>
public sealed record SlaSummaryQuery
{
    public string? From { get; init; }
    public string? To { get; init; }
    public string GroupBy { get; init; } = "agent";

    public static bool TryUtc(string? value, out DateTime utc)
    {
        utc = default;
        if (value is null || value.Length > 40 || !Regex.IsMatch(value,
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            return false;
        }
        utc = timestamp.UtcDateTime;
        return true;
    }
}

public enum SlaSummaryGrouping { Agent, Priority }

public sealed record SlaSummaryRange(DateTime FromUtc, DateTime ToUtc, SlaSummaryGrouping GroupBy);

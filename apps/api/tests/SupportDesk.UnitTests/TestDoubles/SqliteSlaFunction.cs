using System.Globalization;
using Microsoft.Data.Sqlite;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.TestDoubles;

internal static class SqliteSlaFunction
{
    public static void Register(SqliteConnection connection) =>
        connection.CreateFunction<string?, string?, string?, string, double, int>("TicketSlaStatus",
            (due, resolved, started, now, threshold) => (int)SlaEvaluator.Evaluate(
                Parse(due), Parse(resolved), Parse(started), Parse(now)!.Value, (decimal)threshold));

    private static DateTime? Parse(string? text) => text is null ? null
        : DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

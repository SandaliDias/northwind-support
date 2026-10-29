using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQL")))
        {
            Skip = "Set SUPPORTDESK_TEST_SQL to a local SQL Server connection with create/drop database permission.";
        }
    }
}

/// <summary>Every test creates and removes only its own uniquely named database.</summary>
public sealed class SqlServerTask2Tests
{
    internal static SupportDbContext NewDatabase()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQL"))
        { InitialCatalog = "SupportDesk_Task2_" + Guid.NewGuid().ToString("N") };
        return new(new DbContextOptionsBuilder<SupportDbContext>().UseSqlServer(connection.ConnectionString).Options);
    }

    internal static async Task RemoveTestDatabase(SupportDbContext db)
    {
        Assert.StartsWith("SupportDesk_Task2_", db.Database.GetDbConnection().Database);
        await db.Database.EnsureDeletedAsync();
    }

    [SqlServerFact]
    public async Task FreshMigrationSeedsAndSqlSlaMatchesPureEvaluatorWithCorrectPaging()
    {
        await using var db = NewDatabase();
        try
        {
            await db.Database.MigrateAsync();
            var clock = new FixedClock();
            await new SupportDbSeeder(db, clock, NullLogger<SupportDbSeeder>.Instance).SeedAsync();
            Assert.Equal(40, await db.Set<Ticket>().CountAsync());
            var customerId = await db.Set<SupportDesk.Domain.Aggregates.Customers.Customer>().Select(c => c.Id).FirstAsync();
            var categoryId = await db.Set<SupportDesk.Domain.Aggregates.Categories.Category>().Select(c => c.Id).FirstAsync();
            var now = clock.UtcNow;
            var cases = new (DateTime Start, DateTime? Due, DateTime? Resolved)[]
            {
                (now, null, null),
                (now, now.AddHours(4), null),
                (now.AddHours(-3), now.AddHours(1), null),
                (now.AddHours(-4), now, null),
                (now.AddHours(-4), now.AddMilliseconds(-1), null),
                (now.AddHours(-4), now, now),
                (now.AddHours(-4), now, now.AddMilliseconds(1)),
                (now.AddHours(-3).AddMilliseconds(1), now.AddHours(1).AddMilliseconds(1), null),
            };
            var expected = new Dictionary<string, SlaStatus>();
            for (var i = 0; i < cases.Length; i++)
            {
                var row = cases[i];
                var reference = $"SQL-{i}";
                var ticket = Ticket.Raise(reference, "SQL parity boundary", "Database provider integration case.",
                    customerId, categoryId, TicketPriority.Medium, row.Start);
                if (row.Due is not null) ticket.ApplyTriage(row.Due.Value, null, row.Start);
                if (row.Resolved is not null) ticket.ChangeStatus(TicketStatus.Resolved, row.Resolved.Value);
                db.Add(ticket);
                expected[reference] = SlaEvaluator.Evaluate(row.Due, row.Resolved, row.Start, now, 25);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var queries = new TicketQueries(db, clock);
            foreach (var status in Enum.GetValues<SlaStatus>())
            {
                var filter = new TicketQuery
                {
                    Search = "SQL parity boundary",
                    Priority = TicketPriority.Medium,
                    SlaStatus = status,
                    PageSize = 1
                };
                var count = expected.Count(e => e.Value == status);
                var result = await queries.GetPagedAsync(filter, default);
                Assert.Equal(count, result.TotalCount);
                Assert.Equal(count, result.TotalPages);
                Assert.All(result.Items, t => Assert.Equal(expected[t.Reference], t.SlaStatus));
                if (count > 1)
                {
                    var last = await queries.GetPagedAsync(filter with { Page = count }, default);
                    Assert.Single(last.Items);
                    Assert.Equal(count, last.TotalCount);
                }
            }
            var sql = db.Set<Ticket>().Where(t => SupportDbContext.TicketSlaStatus(t.DueAtUtc,
                t.ResolvedAtUtc, t.SlaStartedAtUtc, now, 25) == (int)SlaStatus.AtRisk).ToQueryString();
            Assert.Contains("[dbo].[TicketSlaStatus]", sql);
            Assert.Contains("WHERE", sql);
            clock.Advance(TimeSpan.FromTicks(1));
            var afterBoundary = await queries.GetPagedAsync(new TicketQuery { Search = "SQL parity boundary" }, default);
            foreach (var item in afterBoundary.Items)
            {
                var row = cases[int.Parse(item.Reference[4..])];
                Assert.Equal(SlaEvaluator.Evaluate(row.Due, row.Resolved, row.Start, clock.UtcNow, 25), item.SlaStatus);
            }
            clock.Advance(TimeSpan.FromTicks(-1));
            // Fractional configuration and time just outside/inside the boundary use the same SQL semantics.
            foreach (var threshold in new[] { 10m, 33.33333333m, 100m })
            {
                var configured = new TicketQueries(db, clock, new SlaPolicy { AtRiskThresholdPercent = threshold });
                var results = await configured.GetPagedAsync(new TicketQuery { Search = "SQL parity boundary" }, default);
                foreach (var item in results.Items)
                {
                    var i = int.Parse(item.Reference[4..]);
                    var row = cases[i];
                    Assert.Equal(SlaEvaluator.Evaluate(row.Due, row.Resolved, row.Start, now, threshold), item.SlaStatus);
                }
            }
        }
        finally { await RemoveTestDatabase(db); }
    }

    [SqlServerFact]
    public async Task ExistingStarterDatabaseIsBackfilledWithoutLosingItsTicket()
    {
        await using var db = NewDatabase();
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20260926183612_InitialCreate");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Categories(Name, IsActive, RequiresSpecialist, ForcesCriticalPriority) VALUES ('General',1,0,0);
                INSERT INTO Customers(Name, Email, Tier, CreatedAtUtc) VALUES ('Legacy customer','legacy@example.com',1,'2026-03-02T09:00:00');
                INSERT INTO Tickets(Reference,Title,Description,CustomerId,CategoryId,Priority,Status,CreatedAtUtc,UpdatedAtUtc,DueAtUtc)
                VALUES ('LEGACY-1','Legacy ticket','Existing starter ticket.',1,1,2,1,'2026-03-02T09:00:00','2026-03-02T09:00:00','2026-03-03T09:00:00');
                """);
            await db.Database.MigrateAsync();
            var ticket = await db.Set<Ticket>().SingleAsync();
            Assert.Equal(ticket.CreatedAtUtc, ticket.SlaStartedAtUtc);
            Assert.NotEqual(Guid.Empty, ticket.Version);
            Assert.Equal("LEGACY-1", ticket.Reference);
            var queries = new TicketQueries(db, new FixedClock(ticket.CreatedAtUtc));
            Assert.Equal(SlaStatus.WithinSla, (await queries.GetDetailAsync(ticket.Id, default))!.SlaStatus);
        }
        finally { await RemoveTestDatabase(db); }
    }
}

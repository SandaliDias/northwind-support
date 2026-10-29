using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class SlaReportSqlServerTests
{
    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [SqlServerFact]
    public async Task BothGroupingsAggregateOnceInSqlWithUtcBoundariesAllStatesAndFractionalDurations()
    {
        await using var identity = SqlServerTask2Tests.NewDatabase();
        var capture = new CommandCapture();
        await using var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlServer(identity.Database.GetConnectionString()).AddInterceptors(capture).Options);
        try
        {
            await db.Database.MigrateAsync();
            var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);
            var now = from.AddDays(19).AddHours(12);
            var clock = new Mock<IClock>();
            clock.SetupGet(c => c.UtcNow).Returns(now);
            var policy = new SlaPolicy();
            var customer = new Customer("Report customer", "report@example.com", null, CustomerTier.Standard, from);
            var category = new Category("Report category", false, false);
            // Names are deliberately equal: grouping must retain distinct agent identities.
            var alex = new Agent("Shared name", "report-alex@example.com", 10, from);
            var ben = new Agent("Shared name", "report-ben@example.com", 10, from);
            ben.Deactivate(); // Historical/current ownership still belongs in a report.
            db.AddRange(customer, category, alex, ben);
            await db.SaveChangesAsync();
            Ticket Add(string reference, DateTime created, TicketPriority priority, int? agent,
                DateTime? due = null, DateTime? resolved = null)
            {
                var ticket = Ticket.Raise(reference, "Report fixture", "Report aggregation fixture.",
                    customer.Id, category.Id, priority, created);
                if (due.HasValue) ticket.ApplyTriage(due.Value, agent, created);
                else ticket.AssignTo(agent, created);
                if (resolved.HasValue) ticket.ChangeStatus(TicketStatus.Closed, resolved.Value);
                db.Add(ticket);
                return ticket;
            }
            Add("R-BREACH", from, TicketPriority.Low, alex.Id, from.AddHours(1));
            Add("R-RISK", now.AddHours(-3), TicketPriority.Low, alex.Id, now.AddHours(1));
            Add("R-WITHIN", now, TicketPriority.High, ben.Id, now.AddHours(4));
            var metCreated = from.AddDays(1).AddHours(23.5);
            Add("R-MET", metCreated, TicketPriority.High, alex.Id, metCreated.AddHours(4), metCreated.AddHours(2.5));
            var lateCreated = from.AddDays(2).AddHours(21.5);
            Add("R-LATE", lateCreated, TicketPriority.Low, ben.Id, lateCreated.AddHours(1), lateCreated.AddHours(4.25));
            Add("R-NONE", from, TicketPriority.Medium, null);
            Add("R-NONE-RES", from.AddDays(3), TicketPriority.Medium, null,
                resolved: from.AddDays(3).AddMinutes(30).AddMilliseconds(1));
            Add("R-BEFORE", from.AddMilliseconds(-1), TicketPriority.Critical, null);
            Add("R-AT-TO", to, TicketPriority.Critical, null);
            var longCreated = from.AddYears(-100);
            Add("R-LONG", longCreated, TicketPriority.Critical, null, resolved: from.AddMilliseconds(1));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var queries = new SlaReportQueries(db, clock.Object, policy);
            async Task<IReadOnlyList<SlaSummaryGroupDto>> Read(SlaSummaryRange range)
            {
                capture.Commands.Clear();
                clock.Invocations.Clear();
                var rows = await queries.GetSummaryAsync(range, default);
                var sql = Assert.Single(capture.Commands);
                Assert.Contains("GROUP BY", sql);
                Assert.Contains("AVG(", sql);
                Assert.Contains("COUNT(", sql);
                Assert.Contains("ROUND(", sql);
                Assert.Contains("[dbo].[TicketSlaStatus]", sql);
                Assert.Contains("WHERE", sql);
                Assert.Contains("LEFT JOIN", sql);
                Assert.DoesNotContain("[Description]", sql);
                Assert.DoesNotContain("[PasswordHash]", sql);
                clock.VerifyGet(c => c.UtcNow, Times.Once);
                Assert.Empty(db.ChangeTracker.Entries());
                Assert.All(rows, row => Assert.Equal(row.Total,
                    row.Breached + row.AtRisk + row.Within + row.Met + row.NotApplicable));
                return rows;
            }
            var range = new SlaSummaryRange(from, to, SlaSummaryGrouping.Agent);
            var agents = await Read(range);
            Assert.Equal(new int?[] { null, alex.Id, ben.Id }, agents.Select(row => row.AgentId));
            var unassigned = agents[0];
            Assert.Equal("Unassigned", unassigned.AgentName);
            Assert.Equal(2, unassigned.Total);
            Assert.Equal(2, unassigned.NotApplicable);
            Assert.Equal(0, unassigned.BreachRate);
            Assert.Equal(0.5 + 1.0 / 3600000, unassigned.AverageResolutionHours!.Value, 8);
            var alexRow = agents[1];
            Assert.Equal(3, alexRow.Total);
            Assert.Equal(1, alexRow.Breached);
            Assert.Equal(1, alexRow.AtRisk);
            Assert.Equal(1, alexRow.Met);
            Assert.Equal(33.33, alexRow.BreachRate);
            Assert.Equal(2.5, alexRow.AverageResolutionHours);
            Assert.Equal(2, agents[2].Total);
            Assert.Equal(1, agents[2].Within);
            Assert.Equal(1, agents[2].Breached);
            Assert.Equal(50, agents[2].BreachRate);
            Assert.Equal(4.25, agents[2].AverageResolutionHours);
            Assert.All(agents, row => Assert.Null(row.Priority));

            var priorities = await Read(range with { GroupBy = SlaSummaryGrouping.Priority });
            Assert.Equal(new[] { TicketPriority.Low, TicketPriority.Medium, TicketPriority.High },
                priorities.Select(row => row.Priority!.Value));
            Assert.Equal(66.67, priorities[0].BreachRate);
            Assert.Equal(2, priorities[0].Breached);
            Assert.Equal(1, priorities[0].AtRisk);
            Assert.Equal(4.25, priorities[0].AverageResolutionHours);
            Assert.Equal(0, priorities[2].BreachRate);
            Assert.Equal(1, priorities[2].Within);
            Assert.Equal(1, priorities[2].Met);
            Assert.All(priorities, row => { Assert.Null(row.AgentId); Assert.Null(row.AgentName); });

            policy.AtRiskThresholdPercent = 10;
            var changedThreshold = await Read(range);
            Assert.Equal(0, changedThreshold[1].AtRisk);
            Assert.Equal(1, changedThreshold[1].Within);
            clock.SetupGet(c => c.UtcNow).Returns(now.AddHours(1).AddMilliseconds(1));
            var changedNow = await Read(range);
            Assert.Equal(2, changedNow[1].Breached);

            var unresolvedOnly = await Read(new SlaSummaryRange(now, now.AddMilliseconds(1), SlaSummaryGrouping.Agent));
            Assert.Null(Assert.Single(unresolvedOnly).AverageResolutionHours);
            Assert.Empty(await Read(range with { FromUtc = to.AddMilliseconds(1), ToUtc = to.AddDays(1) }));
            var longRunning = Assert.Single(await Read(new SlaSummaryRange(longCreated, longCreated.AddDays(1), SlaSummaryGrouping.Priority)));
            Assert.Equal((from.AddMilliseconds(1) - longCreated).TotalHours, longRunning.AverageResolutionHours!.Value, 6);
        }
        finally { await SqlServerTask2Tests.RemoveTestDatabase(db); }
    }
}

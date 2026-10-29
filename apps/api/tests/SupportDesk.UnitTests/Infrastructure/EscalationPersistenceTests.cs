using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class EscalationPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<SupportDbContext> _options;
    private readonly SupportDbContext _db;
    private readonly FixedClock _clock = new();
    private readonly Ticket _ticket;
    private readonly Agent _agent;

    public EscalationPersistenceTests()
    {
        _connection.Open();
        SqliteSlaFunction.Register(_connection);
        _options = new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(_connection).Options;
        _db = new(_options);
        _db.Database.EnsureCreated();
        var customer = new Customer("Customer", "customer@example.com", null, CustomerTier.Standard, _clock.UtcNow);
        var category = new Category("General", false, false);
        _agent = new Agent("Agent", "agent@example.com", 10, _clock.UtcNow);
        _db.AddRange(customer, category, _agent);
        _db.SaveChanges();
        _ticket = Ticket.Raise("TCK-TEST", "Persistence case", "Description for persistence testing.",
            customer.Id, category.Id, TicketPriority.Low, _clock.UtcNow);
        _ticket.ApplyTriage(_clock.UtcNow.AddHours(72), _agent.Id, _clock.UtcNow);
        _db.Add(_ticket);
        _db.SaveChanges();
    }

    [Fact]
    public async Task HistoryRoundTripsSnapshotsAndOrdersNewestFirstIncludingTies()
    {
        var store = new EscalationStore(_db);
        Assert.Empty(await store.GetForTicketAsync(_ticket.Id, default));
        var first = _ticket.Escalate(_clock.UtcNow.AddHours(24), _agent.Id, "First escalation", "Lead", _clock.UtcNow);
        await store.AddAsync(first, default);
        await _db.SaveChangesAsync();
        var second = _ticket.Escalate(_clock.UtcNow.AddHours(8), null, "Second escalation", "Other lead", _clock.UtcNow);
        await store.AddAsync(second, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var history = await store.GetForTicketAsync(_ticket.Id, default);
        Assert.Equal(new[] { second.Id, first.Id }, history.Select(h => h.Id));
        Assert.Equal(TicketPriority.Medium, history[0].FromPriority);
        Assert.Equal(TicketPriority.High, history[0].ToPriority);
        Assert.Equal(_agent.Id, history[0].FromAgent!.Id);
        Assert.Null(history[0].ToAgent);
        Assert.Equal(first.ToDueAtUtc, history[0].FromDueAtUtc);
        Assert.Equal("Other lead", history[0].EscalatedBy);
        var reloaded = await _db.Set<Ticket>().SingleAsync();
        Assert.Equal(second.ToDueAtUtc, reloaded.DueAtUtc);
        Assert.Equal(_clock.UtcNow, reloaded.SlaStartedAtUtc);
    }

    [Fact]
    public async Task ConcurrentEscalationRollsBackItsAuditInsert()
    {
        await using var other = new SupportDbContext(_options);
        var stale = await other.Set<Ticket>().SingleAsync();
        var winner = _ticket.Escalate(_clock.UtcNow.AddHours(24), null, "Winner change", "Lead", _clock.UtcNow);
        _db.Add(winner);
        await _db.SaveChangesAsync();
        var loser = stale.Escalate(_clock.UtcNow.AddHours(24), null, "Stale change", "Other lead", _clock.UtcNow);
        other.Add(loser);
        await Assert.ThrowsAsync<ConflictException>(() => other.SaveChangesAsync());
        _db.ChangeTracker.Clear();
        Assert.Single(await _db.Set<TicketEscalation>().ToListAsync());
        Assert.Equal("Winner change", (await _db.Set<TicketEscalation>().SingleAsync()).Reason);
    }

    [Theory]
    [InlineData("known")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("legacy")]
    public async Task HistoryResolvesActorNameWithoutChangingTheImmutableAuditValue(string actorKind)
    {
        if (actorKind == "inactive") _agent.Deactivate();
        var actor = actorKind switch
        {
            "legacy" => "Legacy team lead",
            "missing" => "agent:999999",
            _ => $"agent:{_agent.Id}"
        };
        _db.Add(_ticket.Escalate(_clock.UtcNow.AddHours(24), null, "Actor name case", actor, _clock.UtcNow));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var result = Assert.Single(await new EscalationStore(_db).GetForTicketAsync(_ticket.Id, default));
        Assert.Equal(actor, result.EscalatedBy);
        if (actorKind is "known" or "inactive")
        {
            Assert.Equal(_agent.Id, result.EscalatedByAgent!.Id);
            Assert.Equal(_agent.FullName, result.EscalatedByAgent.FullName);
        }
        else Assert.Null(result.EscalatedByAgent);
        Assert.Equal(actor, (await _db.Set<TicketEscalation>().SingleAsync()).EscalatedBy);
    }

    [Fact]
    public async Task HistoryCannotBeUpdatedOrDeletedThroughTheUnitOfWork()
    {
        var history = _ticket.Escalate(_clock.UtcNow.AddHours(24), null, "Audit row", "Lead", _clock.UtcNow);
        _db.Add(history);
        await _db.SaveChangesAsync();
        _db.Entry(history).Property(e => e.Reason).CurrentValue = "Changed audit row";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
        _db.Entry(history).Reload();
        _db.Remove(history);
        Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
    }

    [Fact]
    public async Task AssignmentCountsExcludeOnlyResolvedAndClosed()
    {
        var candidates = await new AssignmentQueries(_db).GetCandidatesAsync(_ticket.CategoryId, default);
        Assert.Equal(1, candidates.Single().OpenTicketCount);
        _ticket.ChangeStatus(TicketStatus.InProgress, _clock.UtcNow);
        await _db.SaveChangesAsync();
        Assert.Equal(1, (await new AssignmentQueries(_db).GetCandidatesAsync(_ticket.CategoryId, default)).Single().OpenTicketCount);
        _ticket.ChangeStatus(TicketStatus.Resolved, _clock.UtcNow);
        await _db.SaveChangesAsync();
        Assert.Equal(0, (await new AssignmentQueries(_db).GetCandidatesAsync(_ticket.CategoryId, default)).Single().OpenTicketCount);
        _ticket.ChangeStatus(TicketStatus.Closed, _clock.UtcNow);
        await _db.SaveChangesAsync();
        Assert.Equal(0, (await new AssignmentQueries(_db).GetCandidatesAsync(_ticket.CategoryId, default)).Single().OpenTicketCount);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }
}

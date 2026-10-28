using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SupportDbContext _db;
    private readonly FixedClock _clock = new();
    private readonly TicketQueries _queries;

    private readonly Customer _customer1;
    private readonly Customer _customer2;
    private readonly Category _category1;
    private readonly Category _category2;
    private readonly Agent _agent1;
    private readonly Agent _agent2;

    public TicketQueriesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        SqliteSlaFunction.Register(_connection);

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SupportDbContext(options);
        _db.Database.EnsureCreated();

        _customer1 = new Customer("Contoso Ltd", "contact@contoso.com", null, CustomerTier.Premium, _clock.UtcNow);
        _customer2 = new Customer("Northwind Traders", "support@northwind.com", null, CustomerTier.Standard, _clock.UtcNow);
        _category1 = new Category("Billing", true, false);
        _category2 = new Category("Technical", false, false);
        _agent1 = new Agent("Alex Turner", "alex@supportdesk.local", 10, _clock.UtcNow);
        _agent2 = new Agent("Ben Osei", "ben@supportdesk.local", 8, _clock.UtcNow);

        _db.AddRange(_customer1, _customer2, _category1, _category2, _agent1, _agent2);
        _db.SaveChanges();

        _queries = new TicketQueries(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetPagedAsync_WithPriorityFilter_ReturnsOnlyMatchingPriority()
    {
        // Arrange
        var t1 = CreateTicket("TCK-0001", "Low ticket", _customer1.Id, _category1.Id, TicketPriority.Low);
        var t2 = CreateTicket("TCK-0002", "High ticket 1", _customer1.Id, _category1.Id, TicketPriority.High);
        var t3 = CreateTicket("TCK-0003", "High ticket 2", _customer2.Id, _category2.Id, TicketPriority.High);
        var t4 = CreateTicket("TCK-0004", "Critical ticket", _customer2.Id, _category2.Id, TicketPriority.Critical);
        _db.AddRange(t1, t2, t3, t4);
        await _db.SaveChangesAsync();

        var query = new TicketQuery { Priority = TicketPriority.High };

        // Act
        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(TicketPriority.High, item.Priority));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0002");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0003");
    }

    [Fact]
    public async Task GetPagedAsync_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        // Arrange
        var t1 = CreateTicket("TCK-0001", "New ticket", _customer1.Id, _category1.Id, TicketPriority.Medium);
        var t2 = CreateTicket("TCK-0002", "In progress ticket", _customer1.Id, _category1.Id, TicketPriority.Medium);
        t2.ChangeStatus(TicketStatus.InProgress, _clock.UtcNow);
        var t3 = CreateTicket("TCK-0003", "Resolved ticket", _customer2.Id, _category2.Id, TicketPriority.Medium);
        t3.ChangeStatus(TicketStatus.Resolved, _clock.UtcNow);

        _db.AddRange(t1, t2, t3);
        await _db.SaveChangesAsync();

        var query = new TicketQuery { Status = TicketStatus.InProgress };

        // Act
        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(TicketStatus.InProgress, result.Items.Single().Status);
        Assert.Equal("TCK-0002", result.Items.Single().Reference);
    }

    [Fact]
    public async Task GetPagedAsync_WithUnassignedOnly_ReturnsOnlyTicketsWithoutAgent()
    {
        // Arrange
        var unassigned1 = CreateTicket("TCK-0001", "Unassigned 1", _customer1.Id, _category1.Id, TicketPriority.Medium);
        var assigned = CreateTicket("TCK-0002", "Assigned", _customer1.Id, _category1.Id, TicketPriority.Medium);
        assigned.AssignTo(_agent1.Id, _clock.UtcNow);
        var unassigned2 = CreateTicket("TCK-0003", "Unassigned 2", _customer2.Id, _category2.Id, TicketPriority.Medium);

        _db.AddRange(unassigned1, assigned, unassigned2);
        await _db.SaveChangesAsync();

        var query = new TicketQuery { UnassignedOnly = true };

        // Act
        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
        Assert.DoesNotContain(result.Items, item => item.Reference == "TCK-0002");
    }

    [Fact]
    public async Task GetPagedAsync_WithSearchOnCustomerName_ReturnsMatchingTickets()
    {
        // Arrange
        var t1 = CreateTicket("TCK-0001", "A printer stopped working", _customer1.Id, _category1.Id, TicketPriority.Medium);
        var t2 = CreateTicket("TCK-0002", "A monitor is flickering", _customer2.Id, _category2.Id, TicketPriority.Medium);

        _db.AddRange(t1, t2);
        await _db.SaveChangesAsync();

        var query = new TicketQuery { Search = "Contoso" };

        // Act
        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0001", result.Items.Single().Reference);
        Assert.Equal("Contoso Ltd", result.Items.Single().Customer.Name);
    }

    [Fact]
    public async Task GetPagedAsync_WithSearchOnReferenceOrTitle_ReturnsMatchingTickets()
    {
        // Arrange
        var t1 = CreateTicket("TCK-0042", "Printer is on fire", _customer1.Id, _category1.Id, TicketPriority.Critical);
        var t2 = CreateTicket("TCK-0043", "Network unreachable", _customer2.Id, _category2.Id, TicketPriority.High);

        _db.AddRange(t1, t2);
        await _db.SaveChangesAsync();

        // Search by reference
        var byRef = await _queries.GetPagedAsync(new TicketQuery { Search = "TCK-0042" }, CancellationToken.None);
        Assert.Equal(1, byRef.TotalCount);
        Assert.Equal("TCK-0042", byRef.Items.Single().Reference);

        // Search by title substring
        var byTitle = await _queries.GetPagedAsync(new TicketQuery { Search = "Printer" }, CancellationToken.None);
        Assert.Equal(1, byTitle.TotalCount);
        Assert.Equal("TCK-0042", byTitle.Items.Single().Reference);
    }

    [Fact]
    public async Task GetPagedAsync_WithWhitespaceSearch_IgnoresSearchFilter()
    {
        // Arrange
        var t1 = CreateTicket("TCK-0001", "Ticket A", _customer1.Id, _category1.Id, TicketPriority.Low);
        var t2 = CreateTicket("TCK-0002", "Ticket B", _customer2.Id, _category2.Id, TicketPriority.High);
        _db.AddRange(t1, t2);
        await _db.SaveChangesAsync();

        var query = new TicketQuery { Search = "   " };

        // Act
        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert: both tickets returned because whitespace search is ignored
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_WithCombinationOfFilters_CombinesWithAnd()
    {
        // Arrange
        // Match: High, Customer1, Category1, Unassigned
        var match = CreateTicket("TCK-0001", "Target ticket", _customer1.Id, _category1.Id, TicketPriority.High);

        // Different priority
        var wrongPriority = CreateTicket("TCK-0002", "Low ticket", _customer1.Id, _category1.Id, TicketPriority.Low);

        // Different customer
        var wrongCustomer = CreateTicket("TCK-0003", "Wrong customer", _customer2.Id, _category1.Id, TicketPriority.High);

        // Assigned
        var assigned = CreateTicket("TCK-0004", "Assigned ticket", _customer1.Id, _category1.Id, TicketPriority.High);
        assigned.AssignTo(_agent1.Id, _clock.UtcNow);

        _db.AddRange(match, wrongPriority, wrongCustomer, assigned);
        await _db.SaveChangesAsync();

        var query = new TicketQuery
        {
            Priority = TicketPriority.High,
            CustomerId = _customer1.Id,
            CategoryId = _category1.Id,
            UnassignedOnly = true,
            Search = "Target"
        };

        // Act
        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0001", result.Items.Single().Reference);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersBeforeCountingAndPaging_ComputesCorrectTotalsAndPages()
    {
        // Arrange: 5 tickets matching High priority, 3 with Low
        for (var i = 1; i <= 5; i++)
        {
            _db.Add(CreateTicket($"TCK-001{i}", $"High issue {i}", _customer1.Id, _category1.Id, TicketPriority.High));
        }

        for (var i = 1; i <= 3; i++)
        {
            _db.Add(CreateTicket($"TCK-002{i}", $"Low issue {i}", _customer1.Id, _category1.Id, TicketPriority.Low));
        }

        await _db.SaveChangesAsync();

        var query = new TicketQuery
        {
            Priority = TicketPriority.High,
            Page = 1,
            PageSize = 2
        };

        // Act: Page 1
        var page1 = await _queries.GetPagedAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);

        // Act: Page 3
        var page3 = await _queries.GetPagedAsync(query with { Page = 3 }, CancellationToken.None);
        Assert.Equal(5, page3.TotalCount);
        Assert.Equal(3, page3.TotalPages);
        Assert.Single(page3.Items);
    }

    private Ticket CreateTicket(string reference, string title, int customerId, int categoryId, TicketPriority priority) =>
        Ticket.Raise(reference, title, "Standard test ticket description.", customerId, categoryId, priority, _clock.UtcNow);
}

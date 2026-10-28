using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public sealed class EscalateTicketCommandHandlerTests
{
    private readonly TicketCommandTestContext _context = new();
    private readonly Mock<IEscalationStore> _history = new();
    private TicketEscalation? _recorded;
    private readonly Mock<ICurrentAgent> _current = new();

    private EscalateTicketCommandHandler Handler()
    {
        _current.SetupGet(a => a.AgentId).Returns(4);
        _current.SetupGet(a => a.AuditActor).Returns("agent:4");
        _history.Setup(h => h.AddAsync(It.IsAny<TicketEscalation>(), It.IsAny<CancellationToken>()))
            .Callback<TicketEscalation, CancellationToken>((row, _) => _recorded = row).Returns(Task.CompletedTask);
        _history.Setup(h => h.GetForTicketAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new[] { new TicketEscalationDto(_recorded!.Id, _recorded.FromPriority,
                _recorded.ToPriority, null, null, _recorded.FromDueAtUtc, _recorded.ToDueAtUtc,
                _recorded.Reason, _recorded.EscalatedBy, _recorded.EscalatedAtUtc) });
        return new(_context.Tickets.Object, _context.Customers.Object, _context.Categories.Object,
            new TicketTriageService(_context.Assignments.Object, new SlaPolicy()), _history.Object,
            _context.UnitOfWork.Object, _context.Clock, new GetTicketQueryHandler(_context.TicketQueries.Object), _current.Object);
    }

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Medium, TicketPriority.High, 4)]
    [InlineData(TicketPriority.High, TicketPriority.Critical, 2)]
    public async Task EscalationIncreasesOneLevelAndRestartsPremiumWindow(TicketPriority from, TicketPriority to, int hours)
    {
        var ticket = _context.ExistingTicket(new TicketBuilder().WithPriority(from)
            .WithSla(_context.Clock.UtcNow, _context.Clock.UtcNow.AddHours(36)).AssignedTo(4).Build());
        var oldDue = ticket.DueAtUtc;
        _context.Clock.Advance(TimeSpan.FromHours(5));
        _context.Assignments.Setup(a => a.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AssignmentCandidate(1, "Less busy", true, 10, 0, true), new AssignmentCandidate(4, "Current", true, 6, 2, true) });
        var response = await Handler().HandleAsync(ticket.Id, new("  Customer impact increased  "), default);
        Assert.Equal(to, ticket.Priority);
        Assert.Equal(_context.Clock.UtcNow.AddHours(hours), ticket.DueAtUtc);
        Assert.Equal(_context.Clock.UtcNow, ticket.SlaStartedAtUtc);
        Assert.Equal(4, ticket.AssignedAgentId);
        Assert.NotNull(_recorded);
        Assert.Equal(from, _recorded.FromPriority);
        Assert.Equal(to, _recorded.ToPriority);
        Assert.Equal(4, _recorded.FromAgentId);
        Assert.Equal(4, _recorded.ToAgentId);
        Assert.Equal(oldDue, _recorded.FromDueAtUtc);
        Assert.Equal(ticket.DueAtUtc, _recorded.ToDueAtUtc);
        Assert.Equal("Customer impact increased", _recorded.Reason);
        Assert.Equal("agent:4", _recorded.EscalatedBy);
        Assert.Equal(_context.Clock.UtcNow, response.Escalation.EscalatedAtUtc);
        _context.VerifySaved(Times.Once());
        _history.Verify(h => h.AddAsync(It.IsAny<TicketEscalation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(true, 6, true)]
    [InlineData(true, 1, false)]
    public async Task IneligibleOwnerIsReassigned(bool active, int open, bool specialist)
    {
        var ticket = _context.ExistingTicket(new TicketBuilder().AssignedTo(4).Build());
        _context.Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category("Specialist flag", true, false));
        _context.Assignments.Setup(a => a.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AssignmentCandidate(4, "Old", active, 6, open, specialist), new AssignmentCandidate(2, "Next", true, 8, 3, true) });
        await Handler().HandleAsync(ticket.Id, new("Needs escalation"), default);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(4, _recorded!.FromAgentId);
        Assert.Equal(2, _recorded.ToAgentId);
    }

    [Fact]
    public async Task NoCandidateStillEscalatesAndRecordsUnassignment()
    {
        var ticket = _context.ExistingTicket(new TicketBuilder().AssignedTo(4).Build());
        var result = await Handler().HandleAsync(ticket.Id, new("Needs escalation"), default);
        Assert.Null(ticket.AssignedAgentId);
        Assert.Null(_recorded!.ToAgentId);
        Assert.Contains("Unassigned because", result.Ticket.Triage!.AssignmentReason);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.New, "Critical")]
    [InlineData(TicketPriority.Low, TicketStatus.Resolved, "Resolved")]
    [InlineData(TicketPriority.Low, TicketStatus.Closed, "Closed")]
    public async Task RejectedStateDoesNotMutateOrWrite(TicketPriority priority, TicketStatus status, string message)
    {
        var ticket = _context.ExistingTicket(new TicketBuilder().WithPriority(priority).WithStatus(status).Build());
        var version = ticket.Version;
        var exception = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Handler().HandleAsync(ticket.Id, new("Needs escalation"), default));
        Assert.Contains(message, exception.Message);
        Assert.Equal(priority, ticket.Priority);
        Assert.Equal(version, ticket.Version);
        _context.VerifySaved(Times.Never());
        _history.Verify(h => h.AddAsync(It.IsAny<TicketEscalation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("four")]
    public async Task InvalidInputNeverLoadsOrSaves(string reason)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Handler().HandleAsync(1, new(reason), default));
        _context.Tickets.Verify(t => t.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _context.VerifySaved(Times.Never());
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public async Task ValidatorChecksExactTrimmedLengths(int length, bool valid)
    {
        var result = await new EscalateTicketRequestValidator().ValidateAsync(new EscalateTicketRequest(" " + new string('x', length) + " "));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task MissingTicketIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(999, new("Needs escalation"), default));
        _context.VerifySaved(Times.Never());
    }
}

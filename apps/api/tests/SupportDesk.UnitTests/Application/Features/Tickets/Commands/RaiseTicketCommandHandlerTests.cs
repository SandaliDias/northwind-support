using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportDesk.Domain.Aggregates.Customers.Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task CreateUsesCategoryFlagsAndReturnsUnassignedDecision()
    {
        var context = new TicketCommandTestContext();
        context.Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportDesk.Domain.Aggregates.Categories.Category("Arbitrary flagged category", true, true));
        var result = await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low), default);
        Assert.Equal(TicketPriority.Critical, context.AddedTicket!.Priority);
        Assert.Equal(context.Clock.UtcNow.AddHours(2), context.AddedTicket.DueAtUtc);
        Assert.Null(context.AddedTicket.AssignedAgentId);
        Assert.Contains("forces Critical", result.Triage!.PriorityReason);
        Assert.Contains("Unassigned because", result.Triage.AssignmentReason);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task CreateAssignsEligibleAgent()
    {
        var context = new TicketCommandTestContext();
        context.ExistingAgent(4, "Sara");
        await context.RaiseTicket.HandleAsync(Request(), default);
        Assert.Equal(4, context.AddedTicket!.AssignedAgentId);
        Assert.Equal(context.Clock.UtcNow.AddHours(12), context.AddedTicket.DueAtUtc);
    }
}

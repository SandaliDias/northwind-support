using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TriagePolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Low, 72, 36)]
    [InlineData(TicketPriority.Medium, 24, 12)]
    [InlineData(TicketPriority.High, 8, 4)]
    [InlineData(TicketPriority.Critical, 4, 2)]
    public void WindowsFollowPriorityAndTier(TicketPriority priority, int standard, int premium)
    {
        var policy = new SlaPolicy();
        Assert.Equal(TimeSpan.FromHours(standard), policy.Window(priority, CustomerTier.Standard));
        Assert.Equal(TimeSpan.FromHours(premium), policy.Window(priority, CustomerTier.Premium));
    }

    [Fact]
    public void ConfigurationControlsFloorMultiplierAndPriority()
    {
        var policy = new SlaPolicy { PremiumCustomerMultiplier = 0.01, MinimumWindowHours = 1.5 };
        Assert.Equal(TimeSpan.FromHours(1.5), policy.Window(TicketPriority.Critical, CustomerTier.Premium));
        Assert.Equal(TicketPriority.Medium, SlaPolicy.Priority(null, false));
        Assert.Equal(TicketPriority.Low, SlaPolicy.Priority(TicketPriority.Low, false));
        Assert.Equal(TicketPriority.Critical, SlaPolicy.Priority(TicketPriority.Low, true));
        Assert.Equal(TicketPriority.Critical, SlaPolicy.Priority(null, true));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(2, 25)]
    [InlineData(0.5, 0)]
    [InlineData(0.5, 101)]
    public void InvalidConfigurationFailsEarly(double multiplier, int threshold) =>
        Assert.Throws<InvalidOperationException>(() => new SlaPolicy
        { PremiumCustomerMultiplier = multiplier, AtRiskThresholdPercent = threshold }.Validate());

    private static AssignmentCandidate Agent(int id, int open = 0, bool active = true, bool specialist = true, int limit = 8) =>
        new(id, $"Agent {id}", active, limit, open, specialist);

    [Fact]
    public void SelectionUsesFewestOpenThenStableId()
    {
        var result = AgentAssignmentPolicy.Select(new[] { Agent(4, 3), Agent(2, 2), Agent(1, 2) }, false);
        Assert.Equal(1, result?.Id);
    }

    [Fact]
    public void SelectionExcludesInactiveFullAndWrongSpecialists()
    {
        var agents = new[] { Agent(1, active: false), Agent(2, 8), Agent(3, specialist: false), Agent(4, 7) };
        Assert.Equal(4, AgentAssignmentPolicy.Select(agents, true)?.Id);
        Assert.Equal(3, AgentAssignmentPolicy.Select(agents, false)?.Id);
        Assert.Null(AgentAssignmentPolicy.Select(agents.Take(3), true));
    }

    [Fact]
    public void EligibleOwnerIsKeptButOwnerAtLimitIsNot()
    {
        var agents = new[] { Agent(1), Agent(2, 7), Agent(3, 8) };
        Assert.Equal(2, AgentAssignmentPolicy.Select(agents, true, 2)?.Id);
        Assert.Equal(1, AgentAssignmentPolicy.Select(agents, true, 3)?.Id);
        Assert.Null(AgentAssignmentPolicy.Select(Array.Empty<AssignmentCandidate>(), false));
    }

    [Theory]
    [InlineData(-1, SlaStatus.WithinSla)]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.AtRisk)]
    public void RiskBoundaryIsInclusive(int milliseconds, SlaStatus expected)
    {
        var start = FixedClock.DefaultNow;
        var due = start.AddHours(4);
        Assert.Equal(expected, SlaEvaluator.Evaluate(due, null, start, start.AddHours(3).AddMilliseconds(milliseconds), 25));
    }

    [Fact]
    public void SlaPrecedenceAndDueBoundaryAreExact()
    {
        var start = FixedClock.DefaultNow;
        var due = start.AddHours(4);
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, due, start, due, 25));
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(due, due, start, due.AddDays(2), 25));
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(due, due.AddMilliseconds(1), start, due, 25));
        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(due, null, start, due, 25));
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(due, null, start, due.AddTicks(1), 25));
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(due, null, start, start.AddHours(3), 10));
    }
}

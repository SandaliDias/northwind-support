using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Reports;

public sealed record SlaSummaryGroupDto
{
    public int? AgentId { get; init; }
    public string? AgentName { get; init; }
    public TicketPriority? Priority { get; init; }
    public int Total { get; init; }
    public int Breached { get; init; }
    public int AtRisk { get; init; }
    public int Within { get; init; }
    public int Met { get; init; }
    public int NotApplicable { get; init; }
    /// <summary>Breached / Total * 100, rounded to two decimal places, including no-SLA tickets in Total.</summary>
    public double BreachRate { get; init; }
    /// <summary>Creation to resolution, including no-SLA resolutions; null when no ticket is resolved.</summary>
    public double? AverageResolutionHours { get; init; }
}

using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

public sealed record TriageDto(string PriorityReason, string SlaWindowReason, string AssignmentReason);

public sealed record TicketEscalationDto(int Id, TicketPriority FromPriority, TicketPriority ToPriority,
    AgentSummaryDto? FromAgent, AgentSummaryDto? ToAgent, DateTime? FromDueAtUtc, DateTime? ToDueAtUtc,
    string Reason, string EscalatedBy, DateTime EscalatedAtUtc);

public sealed record EscalateTicketRequest(string Reason);

public sealed record EscalateTicketResponse(TicketDetailDto Ticket, TicketEscalationDto Escalation);

using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Abstractions;

public interface IEscalationStore
{
    Task AddAsync(TicketEscalation escalation, CancellationToken ct);
    Task<IReadOnlyList<TicketEscalationDto>> GetForTicketAsync(int ticketId, CancellationToken ct);
}

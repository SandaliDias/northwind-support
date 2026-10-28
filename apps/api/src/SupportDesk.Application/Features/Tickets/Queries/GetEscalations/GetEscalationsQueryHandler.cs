using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Application.Features.Tickets.Queries.GetEscalations;

public sealed class GetEscalationsQueryHandler(ITicketQueries tickets, IEscalationStore history)
{
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int id, CancellationToken ct)
    {
        if (await tickets.GetDetailAsync(id, ct) is null)
        {
            throw new NotFoundException("Ticket", id);
        }

        return await history.GetForTicketAsync(id, ct);
    }
}

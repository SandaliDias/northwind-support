using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketCommandHandler(ITicketRepository tickets,
    ICustomerRepository customers, ICategoryRepository categories, TicketTriageService triage,
    IEscalationStore history, IUnitOfWork unitOfWork, IClock clock, GetTicketQueryHandler getTicket)
{
    public async Task<EscalateTicketResponse> HandleAsync(int id, EscalateTicketRequest request, CancellationToken ct)
    {
        // Also protect direct application callers; the HTTP validation filter uses the same validator.
        var validation = await new EscalateTicketRequestValidator().ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);
        ticket.EnsureCanEscalate();
        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);
        var now = clock.UtcNow;
        var decision = await triage.DecideAsync(category, customer, ticket.Priority + 1, now, ct,
            ticket.AssignedAgentId, escalation: true);
        var escalation = ticket.Escalate(decision.DueAtUtc, decision.AgentId, request.Reason, request.EscalatedBy, now);
        await history.AddAsync(escalation, ct);
        // EF commits the ticket update and audit insert in one transaction, with a concurrency check.
        await unitOfWork.SaveChangesAsync(ct);
        var detail = await getTicket.HandleAsync(id, ct);
        detail.Triage = decision.Explanation;
        var recorded = (await history.GetForTicketAsync(id, ct)).Single(e => e.Id == escalation.Id);
        return new(detail, recorded);
    }
}

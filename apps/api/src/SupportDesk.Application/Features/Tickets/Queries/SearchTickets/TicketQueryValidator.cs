using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;
namespace SupportDesk.Application.Features.Tickets.Queries.SearchTickets;

public sealed class TicketQueryValidator : AbstractValidator<TicketQuery>
{
    public TicketQueryValidator()
    {
        RuleFor(x => x.SlaStatus).IsInEnum().When(x => x.SlaStatus is not null);
    }
}

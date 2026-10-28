using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;
namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 5 and <= 500)
            .WithMessage("Reason must contain 5-500 characters after trimming.");
        RuleFor(x => x.EscalatedBy).Must(a => a is not null && a.Trim().Length is >= 1 and <= 100)
            .WithMessage("An actor of 1-100 characters is required.");
    }
}

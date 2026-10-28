using FluentValidation;
using SupportDesk.Application.Contracts.Auth;
namespace SupportDesk.Application.Features.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).Must(e => e is not null && e.Trim().Length is > 0 and <= 256)
            .WithMessage("Enter an email address of at most 256 characters.")
            .EmailAddress();
        RuleFor(x => x.Password).Must(p => p is not null && p.Length is >= 1 and <= 1024)
            .WithMessage("Enter a password of 1-1024 characters.");
    }
}

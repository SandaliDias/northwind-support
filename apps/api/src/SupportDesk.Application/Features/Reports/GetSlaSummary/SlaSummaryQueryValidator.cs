using FluentValidation;
using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Features.Reports.GetSlaSummary;

public sealed class SlaSummaryQueryValidator : AbstractValidator<SlaSummaryQuery>
{
    public SlaSummaryQueryValidator()
    {
        RuleFor(x => x.From).Must(value => SlaSummaryQuery.TryUtc(value, out _))
            .WithMessage("From must be an ISO 8601 timestamp with Z or an explicit offset, e.g. 2026-10-01T00:00:00Z.");
        RuleFor(x => x.To).Must(value => SlaSummaryQuery.TryUtc(value, out _))
            .WithMessage("To must be an ISO 8601 timestamp with Z or an explicit offset.");
        RuleFor(x => x.To).Must((query, value) =>
                !SlaSummaryQuery.TryUtc(query.From, out var from) ||
                !SlaSummaryQuery.TryUtc(value, out var to) || from < to)
            .WithMessage("To must be later than From.");
        RuleFor(x => x.GroupBy).Must(value =>
                string.Equals(value, "agent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "priority", StringComparison.OrdinalIgnoreCase))
            .WithMessage("GroupBy must be agent or priority.");
    }
}

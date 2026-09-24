using FluentValidation;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Enums;

namespace Sunset.Application.Validators.Moderation;

public class ResolveReportRequestValidator : AbstractValidator<ResolveReportRequest>
{
    public ResolveReportRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum()
            .NotEqual(ReportStatus.Pending)
            .WithMessage("Status must be Resolved or Dismissed.");
    }
}

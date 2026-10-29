using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class BanUserRequestValidator : AbstractValidator<BanUserRequest>
{
    public BanUserRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.Until)
            .Must(until => until!.Value.ToUniversalTime() > DateTime.UtcNow)
            .WithMessage("Until must be in the future.")
            .Must(until => until!.Value.ToUniversalTime() <= DateTime.UtcNow.AddYears(10))
            .WithMessage("Until must be within 10 years (omit it for a permanent ban).")
            .When(x => x.Until is not null);
    }
}

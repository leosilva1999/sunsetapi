using FluentValidation;
using Sunset.Application.DTOs.Auth;

namespace Sunset.Application.Validators.Auth;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);

        RuleFor(x => x.TermsVersion).GreaterThanOrEqualTo(1).When(x => x.TermsVersion.HasValue);
        RuleFor(x => x.PrivacyVersion).GreaterThanOrEqualTo(1).When(x => x.PrivacyVersion.HasValue);
    }
}

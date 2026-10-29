using FluentValidation;
using Sunset.Application.DTOs.Auth;

namespace Sunset.Application.Validators.Auth;

public class GoogleAuthRequestValidator : AbstractValidator<GoogleAuthRequest>
{
    public GoogleAuthRequestValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty();

        RuleFor(x => x.TermsVersion).GreaterThanOrEqualTo(1).When(x => x.TermsVersion.HasValue);
        RuleFor(x => x.PrivacyVersion).GreaterThanOrEqualTo(1).When(x => x.PrivacyVersion.HasValue);
    }
}

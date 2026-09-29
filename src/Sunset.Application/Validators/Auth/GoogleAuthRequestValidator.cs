using FluentValidation;
using Sunset.Application.DTOs.Auth;

namespace Sunset.Application.Validators.Auth;

public class GoogleAuthRequestValidator : AbstractValidator<GoogleAuthRequest>
{
    public GoogleAuthRequestValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty();
    }
}

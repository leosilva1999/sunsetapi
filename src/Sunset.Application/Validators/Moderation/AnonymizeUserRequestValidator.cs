using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class AnonymizeUserRequestValidator : AbstractValidator<AnonymizeUserRequest>
{
    public AnonymizeUserRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

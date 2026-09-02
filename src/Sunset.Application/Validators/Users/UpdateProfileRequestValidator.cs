using FluentValidation;
using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Validators.Users;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.AvatarUrl)
            .MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("AvatarUrl must be a valid URL.")
            .When(x => x.AvatarUrl is not null);
    }
}

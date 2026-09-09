using FluentValidation;
using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Validators.Users;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(100)
            .OverridePropertyName("Name")
            .When(x => x.Name.IsSet);

        RuleFor(x => x.AvatarUrl.Value)
            .MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("AvatarUrl must be a valid URL.")
            .OverridePropertyName("AvatarUrl")
            .When(x => x.AvatarUrl.IsSet && x.AvatarUrl.Value is not null);

        RuleFor(x => x.Bio.Value)
            .MaximumLength(160)
            .OverridePropertyName("Bio")
            .When(x => x.Bio.IsSet);
    }
}

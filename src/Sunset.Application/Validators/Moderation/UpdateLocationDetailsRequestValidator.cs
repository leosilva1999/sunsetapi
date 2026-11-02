using FluentValidation;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Entities;

namespace Sunset.Application.Validators.Moderation;

public class UpdateLocationDetailsRequestValidator : AbstractValidator<UpdateLocationDetailsRequest>
{
    public UpdateLocationDetailsRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Description is not null || x.Category is not null)
            .WithName("body")
            .WithMessage("Send 'description' and/or 'category'.");

        RuleFor(x => x.Description)
            .MaximumLength(Location.DescriptionMaxLength)
            .When(x => x.Description is not null);

        RuleFor(x => x.Category)
            .IsInEnum()
            .When(x => x.Category is not null);
    }
}

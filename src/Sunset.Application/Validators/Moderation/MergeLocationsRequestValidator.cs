using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class MergeLocationsRequestValidator : AbstractValidator<MergeLocationsRequest>
{
    public MergeLocationsRequestValidator()
    {
        RuleFor(x => x.TargetId).NotEmpty();
    }
}

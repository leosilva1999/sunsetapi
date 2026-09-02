using FluentValidation;
using Sunset.Application.DTOs.Locations;

namespace Sunset.Application.Validators.Locations;

public class CreateRatingRequestValidator : AbstractValidator<CreateRatingRequest>
{
    public CreateRatingRequestValidator()
    {
        RuleFor(x => x.Score)
            .InclusiveBetween(1, 5);
    }
}

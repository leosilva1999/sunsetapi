using FluentValidation;
using Sunset.Application.DTOs.Locations;

namespace Sunset.Application.Validators.Locations;

public class CreateLocationRequestValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150);

        // The catalog city is preferred; the legacy text still works for clients that haven't moved yet.
        RuleFor(x => x.CityId)
            .NotNull()
            .When(x => string.IsNullOrWhiteSpace(x.City))
            .WithMessage("Either 'cityId' or 'city' is required.");

        RuleFor(x => x.City)
            .MaximumLength(100)
            .When(x => x.City is not null);

        RuleFor(x => x.CityId)
            .NotEqual(Guid.Empty)
            .When(x => x.CityId is not null);

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180);
    }
}

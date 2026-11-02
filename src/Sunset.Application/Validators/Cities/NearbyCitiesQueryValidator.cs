using FluentValidation;
using Sunset.Application.DTOs.Cities;

namespace Sunset.Application.Validators.Cities;

public class NearbyCitiesQueryValidator : AbstractValidator<NearbyCitiesQuery>
{
    public NearbyCitiesQueryValidator()
    {
        RuleFor(x => x.Latitude).NotNull().WithMessage("'lat' is required.");
        RuleFor(x => x.Longitude).NotNull().WithMessage("'lng' is required.");
        RuleFor(x => x.Latitude!.Value).InclusiveBetween(-90, 90).When(x => x.Latitude is not null).OverridePropertyName("Latitude");
        RuleFor(x => x.Longitude!.Value).InclusiveBetween(-180, 180).When(x => x.Longitude is not null).OverridePropertyName("Longitude");
        RuleFor(x => x.CityId).NotEqual(Guid.Empty).When(x => x.CityId is not null);
    }
}

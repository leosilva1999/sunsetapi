using FluentValidation;
using Sunset.Application.DTOs.Locations;

namespace Sunset.Application.Validators.Locations;

public class LocationSearchQueryValidator : AbstractValidator<LocationSearchQuery>
{
    public LocationSearchQueryValidator()
    {
        // Each condition gets its own RuleFor: a trailing .When() on a chain replaces the conditions
        // of the validators before it, which would silently drop the "supplied together" check.
        RuleFor(x => x.Latitude)
            .NotNull()
            .When(x => x.Longitude is not null || x.RadiusKm is not null)
            .WithMessage("'lat' and 'lng' must be supplied together.");

        RuleFor(x => x.Longitude)
            .NotNull()
            .When(x => x.Latitude is not null || x.RadiusKm is not null)
            .WithMessage("'lat' and 'lng' must be supplied together.");

        RuleFor(x => x.Latitude!.Value)
            .InclusiveBetween(-90, 90)
            .When(x => x.Latitude is not null)
            .WithName("Latitude");

        RuleFor(x => x.Longitude!.Value)
            .InclusiveBetween(-180, 180)
            .When(x => x.Longitude is not null)
            .WithName("Longitude");

        RuleFor(x => x.RadiusKm!.Value)
            .GreaterThan(0)
            .LessThanOrEqualTo(LocationSearchQuery.MaxRadiusKm)
            .When(x => x.RadiusKm is not null)
            .WithName("RadiusKm");
    }
}

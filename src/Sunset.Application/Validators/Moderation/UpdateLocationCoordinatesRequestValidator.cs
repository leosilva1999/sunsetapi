using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class UpdateLocationCoordinatesRequestValidator : AbstractValidator<UpdateLocationCoordinatesRequest>
{
    public UpdateLocationCoordinatesRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
    }
}

using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class ChangeLocationCityRequestValidator : AbstractValidator<ChangeLocationCityRequest>
{
    public ChangeLocationCityRequestValidator()
    {
        RuleFor(x => x.CityId).NotEmpty();
    }
}

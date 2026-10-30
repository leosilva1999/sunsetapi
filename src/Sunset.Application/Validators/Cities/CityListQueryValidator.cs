using FluentValidation;
using Sunset.Application.DTOs.Cities;

namespace Sunset.Application.Validators.Cities;

public class CityListQueryValidator : AbstractValidator<CityListQuery>
{
    public CityListQueryValidator()
    {
        RuleFor(x => x.Q!.Trim().Length)
            .GreaterThanOrEqualTo(CityListQuery.MinSearchLength)
            .When(x => !string.IsNullOrWhiteSpace(x.Q))
            .OverridePropertyName("Q")
            .WithMessage($"'q' must have at least {CityListQuery.MinSearchLength} characters.");

        RuleFor(x => x.Country)
            .Must(country => country!.Length == 2 && country.All(char.IsLetter))
            .When(x => !string.IsNullOrWhiteSpace(x.Country))
            .WithMessage("'country' must be a 2-letter ISO code.");

        RuleFor(x => x.State)
            .MaximumLength(10)
            .When(x => !string.IsNullOrWhiteSpace(x.State));

        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Sort).IsInEnum();
    }
}

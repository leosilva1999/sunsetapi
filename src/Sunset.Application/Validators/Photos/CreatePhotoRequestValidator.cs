using FluentValidation;
using Sunset.Application.DTOs.Photos;

namespace Sunset.Application.Validators.Photos;

public class CreatePhotoRequestValidator : AbstractValidator<CreatePhotoRequest>
{
    public CreatePhotoRequestValidator()
    {
        RuleFor(x => x.LocationId)
            .NotEmpty();

        RuleFor(x => x.ImageUrl)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ImageUrl must be a valid URL.");

        RuleFor(x => x.Caption)
            .MaximumLength(500);
    }
}

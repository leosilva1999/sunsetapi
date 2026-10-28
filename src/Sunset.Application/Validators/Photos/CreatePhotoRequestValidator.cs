using FluentValidation;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Interfaces;

namespace Sunset.Application.Validators.Photos;

public class CreatePhotoRequestValidator : AbstractValidator<CreatePhotoRequest>
{
    public CreatePhotoRequestValidator(IPhotoStorageService photoStorageService)
    {
        RuleFor(x => x.LocationId)
            .NotEmpty();

        RuleFor(x => x.ImageUrl)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ImageUrl must be a valid URL.")
            .Must(url => photoStorageService.IsValidPhotoUrl(url))
            .WithMessage("ImageUrl must point to a file uploaded via POST /photos/upload-url.");

        RuleFor(x => x.Caption)
            .MaximumLength(500);
    }
}

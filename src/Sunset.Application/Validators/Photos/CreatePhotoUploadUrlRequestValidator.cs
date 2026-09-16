using FluentValidation;
using Sunset.Application.DTOs.Photos;

namespace Sunset.Application.Validators.Photos;

public class CreatePhotoUploadUrlRequestValidator : AbstractValidator<CreatePhotoUploadUrlRequest>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    public CreatePhotoUploadUrlRequestValidator()
    {
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(contentType => AllowedContentTypes.Contains(contentType))
            .WithMessage("ContentType must be image/jpeg or image/png.");
    }
}

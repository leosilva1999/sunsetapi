using FluentValidation;
using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Validators.Users;

public class CreateAvatarUploadUrlRequestValidator : AbstractValidator<CreateAvatarUploadUrlRequest>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    public CreateAvatarUploadUrlRequestValidator()
    {
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(contentType => AllowedContentTypes.Contains(contentType))
            .WithMessage("ContentType must be image/jpeg or image/png.");
    }
}

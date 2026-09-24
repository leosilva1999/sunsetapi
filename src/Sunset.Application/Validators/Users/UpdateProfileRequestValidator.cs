using FluentValidation;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Interfaces;

namespace Sunset.Application.Validators.Users;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator(IAvatarStorageService avatarStorageService)
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty()
            .MaximumLength(100)
            .OverridePropertyName("Name")
            .When(x => x.Name.IsSet);

        // Restrito ao nosso próprio storage (não só "é uma URL válida") - sem isso, PATCH
        // /users/me aceitaria uma URL de rastreamento em domínio arbitrário como avatar, e
        // todo mundo que visse o perfil/comentário/avaliação dessa pessoa teria o navegador
        // fazendo uma requisição pra esse domínio, vazando IP pro dono da URL.
        RuleFor(x => x.AvatarUrl.Value)
            .MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("AvatarUrl must be a valid URL.")
            .Must(url => avatarStorageService.IsValidAvatarUrl(url!))
            .WithMessage("AvatarUrl must point to a file uploaded via POST /users/me/avatar-upload-url.")
            .OverridePropertyName("AvatarUrl")
            .When(x => x.AvatarUrl.IsSet && x.AvatarUrl.Value is not null);

        RuleFor(x => x.Bio.Value)
            .MaximumLength(160)
            .OverridePropertyName("Bio")
            .When(x => x.Bio.IsSet);
    }
}

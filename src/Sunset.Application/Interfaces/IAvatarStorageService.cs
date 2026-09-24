using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Interfaces;

public interface IAvatarStorageService
{
    AvatarUploadUrlResponse CreateUploadUrl(Guid userId, string contentType);

    // Usado pelo UpdateProfileRequestValidator pra impedir PATCH /users/me de aceitar
    // avatarUrl de um domínio arbitrário - só URLs geradas por CreateUploadUrl acima
    // (ou seja, dentro do nosso próprio bucket) passam.
    bool IsValidAvatarUrl(string url);
}

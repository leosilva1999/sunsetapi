using Sunset.Application.DTOs.Photos;

namespace Sunset.Application.Interfaces;

public interface IPhotoStorageService
{
    PhotoUploadUrlResponse CreateUploadUrl(Guid userId, string contentType);

    // Usado pelo CreatePhotoRequestValidator: só URLs dentro do nosso bucket (geradas por
    // CreateUploadUrl) viram foto - mesmo motivo do IsValidAvatarUrl.
    bool IsValidPhotoUrl(string url);
}

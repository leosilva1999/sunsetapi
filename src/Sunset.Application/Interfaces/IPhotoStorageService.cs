using Sunset.Application.DTOs.Photos;

namespace Sunset.Application.Interfaces;

public interface IPhotoStorageService
{
    PhotoUploadUrlResponse CreateUploadUrl(Guid userId, string contentType);
}

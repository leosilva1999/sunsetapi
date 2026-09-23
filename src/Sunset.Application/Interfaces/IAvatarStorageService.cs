using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Interfaces;

public interface IAvatarStorageService
{
    AvatarUploadUrlResponse CreateUploadUrl(Guid userId, string contentType);
}

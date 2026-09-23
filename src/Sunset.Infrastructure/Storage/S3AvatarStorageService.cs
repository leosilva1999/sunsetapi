using Amazon.S3;
using Microsoft.Extensions.Options;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Storage;

public class S3AvatarStorageService(IAmazonS3 s3Client, IOptions<StorageOptions> options) : IAvatarStorageService
{
    public AvatarUploadUrlResponse CreateUploadUrl(Guid userId, string contentType)
    {
        var storage = options.Value;
        var (uploadUrl, objectKey) = S3PresignedUploadUrlFactory.Create(s3Client, storage, $"avatars/{userId:N}", contentType);

        var avatarUrl = $"{storage.PublicBaseUrl.TrimEnd('/')}/{objectKey}";

        return new AvatarUploadUrlResponse(uploadUrl, avatarUrl);
    }
}

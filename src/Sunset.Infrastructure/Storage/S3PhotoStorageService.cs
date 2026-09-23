using Amazon.S3;
using Microsoft.Extensions.Options;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Storage;

public class S3PhotoStorageService(IAmazonS3 s3Client, IOptions<StorageOptions> options) : IPhotoStorageService
{
    public PhotoUploadUrlResponse CreateUploadUrl(Guid userId, string contentType)
    {
        var storage = options.Value;
        var (uploadUrl, objectKey) = S3PresignedUploadUrlFactory.Create(s3Client, storage, $"photos/{userId:N}", contentType);

        var imageUrl = $"{storage.PublicBaseUrl.TrimEnd('/')}/{objectKey}";

        return new PhotoUploadUrlResponse(uploadUrl, imageUrl);
    }
}

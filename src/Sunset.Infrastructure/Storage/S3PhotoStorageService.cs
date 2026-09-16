using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Storage;

public class S3PhotoStorageService(IAmazonS3 s3Client, IOptions<StorageOptions> options) : IPhotoStorageService
{
    private static readonly Dictionary<string, string> ExtensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
    };

    private static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(5);

    public PhotoUploadUrlResponse CreateUploadUrl(Guid userId, string contentType)
    {
        var storage = options.Value;
        var extension = ExtensionsByContentType.GetValueOrDefault(contentType, "bin");
        var objectKey = $"photos/{userId:N}/{Guid.NewGuid():N}.{extension}";

        var uploadUrl = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = storage.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(UploadUrlLifetime),
            ContentType = contentType,
        });
        uploadUrl = MatchServiceUrlScheme(uploadUrl, storage.ServiceUrl);

        var imageUrl = $"{storage.PublicBaseUrl.TrimEnd('/')}/{objectKey}";

        return new PhotoUploadUrlResponse(uploadUrl, imageUrl);
    }

    // GetPreSignedURL() always builds an https:// URL, even with an http:// ServiceURL
    // configured (LocalStack) - AmazonS3Config.UseHttp explicitly doesn't apply once
    // ServiceURL is set, per its own doc comment. Only the scheme is wrong; host/port/path/
    // query (and therefore the signature, which doesn't cover the scheme) stay valid.
    private static string MatchServiceUrlScheme(string presignedUrl, string? serviceUrl)
    {
        if (string.IsNullOrWhiteSpace(serviceUrl))
            return presignedUrl;

        var expectedScheme = new Uri(serviceUrl).Scheme;
        var actualScheme = new Uri(presignedUrl).Scheme;
        return actualScheme == expectedScheme
            ? presignedUrl
            : expectedScheme + presignedUrl[actualScheme.Length..];
    }
}

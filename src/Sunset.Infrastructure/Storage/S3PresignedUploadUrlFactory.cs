using Amazon.S3;
using Amazon.S3.Model;

namespace Sunset.Infrastructure.Storage;

/// <summary>
/// Shared pre-signed PUT URL generation for S3-backed uploads (photos, avatars, ...) - only the
/// object key prefix and the resulting public URL differ per feature.
/// </summary>
internal static class S3PresignedUploadUrlFactory
{
    private static readonly Dictionary<string, string> ExtensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
    };

    private static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(5);

    public static (string UploadUrl, string ObjectKey) Create(IAmazonS3 s3Client, StorageOptions storage, string keyPrefix, string contentType)
    {
        var extension = ExtensionsByContentType.GetValueOrDefault(contentType, "bin");
        var objectKey = $"{keyPrefix}/{Guid.NewGuid():N}.{extension}";

        var uploadUrl = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = storage.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(UploadUrlLifetime),
            ContentType = contentType,
        });

        return (MatchServiceUrlScheme(uploadUrl, storage.ServiceUrl), objectKey);
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

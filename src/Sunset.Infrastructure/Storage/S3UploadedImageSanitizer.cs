using Amazon.S3;
using Amazon.Runtime;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sunset.Application.Common;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Storage;

/// <summary>
/// Downloads the just-uploaded object through its public URL, strips its metadata
/// (<see cref="ImageMetadataStripper"/>) and puts the clean bytes back under the same key. Reading
/// through the public URL keeps the IAM user write-only (s3:PutObject is all it needs).
/// </summary>
public class S3UploadedImageSanitizer(
    HttpClient httpClient,
    IAmazonS3 s3Client,
    IOptions<StorageOptions> options,
    ILogger<S3UploadedImageSanitizer> logger) : IUploadedImageSanitizer
{
    private const int MaxBytes = 15 * 1024 * 1024;

    public async Task<bool> SanitizeAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        await SanitizeAndMeasureAsync(imageUrl, cancellationToken) is not null;

    public async Task<ImageDimensions?> SanitizeAndMeasureAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        var storage = options.Value;
        var prefix = storage.PublicBaseUrl.TrimEnd('/') + "/";

        // Only objects in our own bucket; no query/fragment/traversal tricks in the key.
        if (!imageUrl.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var key = imageUrl[prefix.Length..];
        if (key.Length == 0 || key.IndexOfAny(['?', '#', '\\']) >= 0 || key.Contains(".."))
            return null;

        byte[] original;
        try
        {
            using var response = await httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Nothing was uploaded under that key (or it isn't readable): not a usable image.
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
                return null;
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaxBytes)
                return null;

            var read = await ReadLimitedAsync(response.Content, cancellationToken);
            if (read is null)
                return null;
            original = read;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not read uploaded image {Key} from storage", key);
            throw new ExternalServiceException("Could not read the uploaded image from storage.", ex);
        }

        var clean = ImageMetadataStripper.TryStrip(original, out var contentType);
        if (clean is null)
            return null;

        var dimensions = ImageDimensionReader.TryRead(clean);
        if (dimensions is null)
            return null;

        // Already clean: nothing to rewrite.
        if (clean.AsSpan().SequenceEqual(original))
            return dimensions;

        try
        {
            using var stream = new MemoryStream(clean);
            await s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = storage.BucketName,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
            }, cancellationToken);
        }
        catch (AmazonServiceException ex)
        {
            logger.LogError(ex, "Could not write the cleaned image {Key} back to storage", key);
            throw new ExternalServiceException("Could not save the cleaned image to storage.", ex);
        }

        logger.LogInformation("Stripped image metadata from {Key} ({Before} -> {After} bytes)", key, original.Length, clean.Length);
        return dimensions;
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

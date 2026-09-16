using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Logging;

namespace Sunset.Infrastructure.Storage;

/// <summary>
/// Makes the LocalStack S3 bucket usable on every dev startup: creates it if missing, and
/// (re)applies the CORS rule and public-read policy a fresh LocalStack container won't have.
/// No-op outside Development - see the ServiceUrl guard below.
/// </summary>
public static class LocalStackBucketInitializer
{
    public static async Task EnsureBucketReadyAsync(IAmazonS3 s3Client, StorageOptions options, ILogger logger, CancellationToken cancellationToken = default)
    {
        // ServiceUrl is only ever set for LocalStack (see StorageOptions) - this must never run
        // against real AWS, where the bucket's CORS/policy are managed by infra, not app code.
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
            return;

        if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3Client, options.BucketName))
        {
            await s3Client.PutBucketAsync(new PutBucketRequest { BucketName = options.BucketName }, cancellationToken);
            logger.LogInformation("Created LocalStack bucket '{BucketName}'.", options.BucketName);
        }

        await s3Client.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
        {
            BucketName = options.BucketName,
            Configuration = new CORSConfiguration
            {
                Rules =
                [
                    new CORSRule
                    {
                        AllowedOrigins = ["http://localhost:3000"],
                        AllowedMethods = ["GET", "PUT", "HEAD"],
                        AllowedHeaders = ["*"],
                        MaxAgeSeconds = 3000,
                    },
                ],
            },
        }, cancellationToken);

        // Public read so uploaded photos load directly via imageUrl - fine for a disposable dev
        // bucket, but never do this against a real bucket (that would need a CDN/GET-presign flow).
        var publicReadPolicy = $$"""
            {
              "Version": "2012-10-17",
              "Statement": [
                {
                  "Effect": "Allow",
                  "Principal": "*",
                  "Action": "s3:GetObject",
                  "Resource": "arn:aws:s3:::{{options.BucketName}}/*"
                }
              ]
            }
            """;
        await s3Client.PutBucketPolicyAsync(options.BucketName, publicReadPolicy, cancellationToken);

        logger.LogInformation("LocalStack bucket '{BucketName}' is ready.", options.BucketName);
    }
}

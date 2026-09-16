namespace Sunset.Infrastructure.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string BucketName { get; init; } = null!;
    public string Region { get; init; } = null!;

    // Only set in Development, pointing at LocalStack - null in other environments so the AWS
    // SDK resolves the real S3 endpoint for Region instead.
    public string? ServiceUrl { get; init; }

    // LocalStack (and most S3-compatible services) need path-style URLs (host/bucket/key)
    // instead of AWS's default virtual-hosted style (bucket.host/key).
    public bool ForcePathStyle { get; init; }

    public string PublicBaseUrl { get; init; } = null!;

    // Only set in Development - LocalStack requires *some* credentials to be present (any
    // value works), while real AWS credentials come from the environment/IAM role instead of
    // config, so these stay empty there.
    public string? AccessKey { get; init; }
    public string? SecretKey { get; init; }
}

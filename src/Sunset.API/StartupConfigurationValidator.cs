using Microsoft.Extensions.Configuration;

namespace Sunset.API;

/// <summary>
/// Fails the boot with one clear message instead of letting a misconfiguration surface later as
/// an opaque 500 (a short JWT secret only fails when the first token is signed; missing AWS
/// keys only when the first upload URL is requested, after a ~16 s credential-chain timeout).
/// </summary>
public static class StartupConfigurationValidator
{
    private const int MinJwtSecretBytes = 32;

    public static void ValidateOrThrow(IConfiguration configuration, bool isProduction)
    {
        var problems = Validate(configuration, isProduction);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Invalid configuration:" + string.Concat(problems.Select(p => $"{Environment.NewLine} - {p}")));
    }

    public static List<string> Validate(IConfiguration configuration, bool isProduction)
    {
        var problems = new List<string>();

        // JWT applies to every environment: HS256 needs a Base64 key of more than 256 bits.
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            problems.Add("Jwt:Secret is not set (env var Jwt__Secret).");
        else
        {
            Span<byte> buffer = new byte[secret.Length];
            if (!Convert.TryFromBase64String(secret, buffer, out var bytes))
                problems.Add("Jwt:Secret is not valid Base64 (generate one with: openssl rand -base64 48).");
            else if (bytes < MinJwtSecretBytes)
                problems.Add($"Jwt:Secret decodes to {bytes * 8} bits; HS256 needs at least {MinJwtSecretBytes * 8} bits ({MinJwtSecretBytes} bytes) - generate one with: openssl rand -base64 48.");
        }

        // The rest is only enforced in Production: Development/Testing legitimately run against
        // LocalStack, Mailpit and local databases with their own defaults.
        if (!isProduction)
            return problems;

        Require(configuration, problems, "ConnectionStrings:DefaultConnection", "ConnectionStrings__DefaultConnection");
        Require(configuration, problems, "Frontend:BaseUrl", "Frontend__BaseUrl");

        Require(configuration, problems, "Storage:BucketName", "Storage__BucketName");
        Require(configuration, problems, "Storage:PublicBaseUrl", "Storage__PublicBaseUrl");
        if (string.IsNullOrWhiteSpace(configuration["Storage:ServiceUrl"]))
        {
            // Real AWS (no LocalStack ServiceUrl): without explicit keys the SDK falls back to a
            // credential chain that does not exist on Render/VPS and only fails on first use.
            Require(configuration, problems, "Storage:Region", "Storage__Region");
            Require(configuration, problems, "Storage:AccessKey", "Storage__AccessKey");
            Require(configuration, problems, "Storage:SecretKey", "Storage__SecretKey");
        }

        if (string.Equals(configuration["Email:Provider"], "HostingerApi", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, problems, "Hostinger:Mail:ApiKey", "Hostinger__Mail__ApiKey");
            Require(configuration, problems, "Hostinger:Mail:MailboxResourceId", "Hostinger__Mail__MailboxResourceId");
        }
        else
        {
            Require(configuration, problems, "Smtp:Host", "Smtp__Host (or set Email__Provider=HostingerApi)");
            Require(configuration, problems, "Smtp:FromAddress", "Smtp__FromAddress");
        }

        return problems;
    }

    private static void Require(IConfiguration configuration, List<string> problems, string key, string envVar)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            problems.Add($"{key} is not set (env var {envVar}).");
    }
}

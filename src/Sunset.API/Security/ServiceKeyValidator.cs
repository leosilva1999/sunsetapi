using System.Security.Cryptography;
using System.Text;

namespace Sunset.API.Security;

/// <summary>
/// Recognises the site's own server: it sends <c>X-Service-Key</c> with a secret that only lives in that server's
/// environment. A request carrying the right key gets a rate-limit bucket of its own instead of sharing the
/// server's single IP with every visitor it renders pages for. The key buys capacity, nothing else: it is
/// not authentication and grants no access to any data.
/// </summary>
public sealed class ServiceKeyValidator
{
    public const string HeaderName = "X-Service-Key";

    private readonly byte[]? _key;

    public ServiceKeyValidator(IConfiguration configuration)
    {
        var configured = configuration["Service:ApiKey"];
        _key = string.IsNullOrWhiteSpace(configured) ? null : Encoding.UTF8.GetBytes(configured);
    }

    public bool IsValid(HttpRequest request)
    {
        if (_key is null || !request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1 || values[0] is not { Length: > 0 } provided)
            return false;

        var bytes = Encoding.UTF8.GetBytes(provided);

        // Constant time for equal-length input; a length mismatch is rejected up front (the length alone is not secret).
        return bytes.Length == _key.Length && CryptographicOperations.FixedTimeEquals(bytes, _key);
    }
}

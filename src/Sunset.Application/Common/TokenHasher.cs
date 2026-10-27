using System.Security.Cryptography;
using System.Text;

namespace Sunset.Application.Common;

/// <summary>SHA-256 (hex) used to store refresh/reset tokens - the raw token is never persisted.</summary>
public static class TokenHasher
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

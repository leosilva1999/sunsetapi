using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(User user);
    (string Token, DateTime ExpiresAt) GenerateRefreshToken();

    // Shared by GenerateRefreshToken and anything else that needs an opaque, unguessable,
    // URL-safe random token (password reset, email verification, ...) - callers decide their
    // own expiry.
    string GenerateOpaqueToken();
}

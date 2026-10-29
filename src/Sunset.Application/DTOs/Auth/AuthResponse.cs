using Sunset.Application.DTOs.Legal;
using Sunset.Application.DTOs.Users;

namespace Sunset.Application.DTOs.Auth;

/// <param name="LegalAcceptance">Whether the user still has to accept the current Terms / Privacy Policy
/// (<c>pending</c>); the client shows the acceptance screen and calls POST /users/me/legal-acceptances.</param>
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserResponse User, LegalAcceptanceStatusResponse? LegalAcceptance = null);

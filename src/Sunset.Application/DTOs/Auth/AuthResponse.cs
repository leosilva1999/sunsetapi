using Sunset.Application.DTOs.Users;

namespace Sunset.Application.DTOs.Auth;

public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserResponse User);

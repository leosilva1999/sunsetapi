namespace Sunset.Application.DTOs.Users;

/// <param name="RefreshToken">
/// The caller's current refresh token. When it is valid and belongs to the caller, that session
/// stays signed in and every other session is revoked; when omitted, every session is revoked
/// (including the caller's, who must sign in again).
/// </param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

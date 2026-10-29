namespace Sunset.Application.DTOs.Moderation;

/// <param name="Reason">Shown to the user (login error and notice email).</param>
/// <param name="Until">UTC end of the ban; null = permanent.</param>
public sealed record BanUserRequest(string Reason, DateTime? Until = null);

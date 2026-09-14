namespace Sunset.Application.DTOs.Locations;

public sealed record RatingResponse(
    Guid Id,
    Guid UserId,
    string UserName,
    string? UserAvatarUrl,
    int Score,
    string? Comment,
    DateTime CreatedAt);

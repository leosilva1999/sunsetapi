namespace Sunset.Application.DTOs.Photos;

public sealed record PhotoResponse(
    Guid Id,
    Guid UserId,
    string UserName,
    string? UserAvatarUrl,
    Guid LocationId,
    string LocationName,
    string ImageUrl,
    string? Caption,
    int LikesCount,
    bool LikedByCurrentUser,
    DateTime CreatedAt);

namespace Sunset.Application.DTOs.Photos;

public sealed record CommentResponse(
    Guid Id,
    Guid UserId,
    string UserName,
    string? UserAvatarUrl,
    string Content,
    DateTime CreatedAt);

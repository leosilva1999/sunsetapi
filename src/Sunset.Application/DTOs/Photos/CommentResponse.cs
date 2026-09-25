namespace Sunset.Application.DTOs.Photos;

public sealed record CommentResponse(
    Guid Id,
    Guid PhotoId,
    Guid UserId,
    string UserName,
    string? UserAvatarUrl,
    string Content,
    DateTime CreatedAt,
    Guid? ParentCommentId,
    int RepliesCount);

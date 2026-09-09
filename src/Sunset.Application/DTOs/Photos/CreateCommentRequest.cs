namespace Sunset.Application.DTOs.Photos;

public sealed record CreateCommentRequest(string Content, Guid? ParentCommentId = null);

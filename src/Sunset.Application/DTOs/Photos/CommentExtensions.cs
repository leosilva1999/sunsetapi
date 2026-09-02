using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Photos;

public static class CommentExtensions
{
    public static CommentResponse ToResponse(this Comment comment) =>
        new(comment.Id, comment.UserId, comment.User.Name, comment.User.AvatarUrl, comment.Content, comment.CreatedAt);
}

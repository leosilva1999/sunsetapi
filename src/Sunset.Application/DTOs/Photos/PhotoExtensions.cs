using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Photos;

public static class PhotoExtensions
{
    public static PhotoResponse ToResponse(this Photo photo, bool likedByCurrentUser = false) =>
        new(
            photo.Id,
            photo.UserId,
            photo.User.Name,
            photo.User.AvatarUrl,
            photo.LocationId,
            photo.Location.Name,
            photo.ImageUrl,
            photo.Caption,
            photo.LikesCount,
            likedByCurrentUser,
            photo.CreatedAt);
}

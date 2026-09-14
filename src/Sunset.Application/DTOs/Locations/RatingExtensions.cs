using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Locations;

public static class RatingExtensions
{
    public static RatingResponse ToResponse(this Rating rating) =>
        new(rating.Id, rating.UserId, rating.User.Name, rating.User.AvatarUrl, rating.Score, rating.Comment, rating.CreatedAt);
}

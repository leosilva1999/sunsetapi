using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Users;

public static class UserExtensions
{
    public static UserResponse ToResponse(this User user) =>
        new(user.Id, user.Name, user.Email, user.AvatarUrl, user.CreatedAt);
}

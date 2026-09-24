using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    UserRole? Role { get; }
    bool IsModerator { get; }
}

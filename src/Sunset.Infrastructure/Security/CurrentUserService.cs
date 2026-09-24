using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.Infrastructure.Security;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public UserRole? Role
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue("role");
            return Enum.TryParse<UserRole>(value, out var role) ? role : null;
        }
    }

    public bool IsModerator => Role is UserRole.Moderator or UserRole.Admin;
}

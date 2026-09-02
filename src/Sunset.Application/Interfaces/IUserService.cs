using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Interfaces;

public interface IUserService
{
    Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
}

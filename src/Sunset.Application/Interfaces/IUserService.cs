using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;

namespace Sunset.Application.Interfaces;

public interface IUserService
{
    Task<PublicUserResponse> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken cancellationToken = default);
    Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);

    Task FollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken = default);
    Task UnfollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<PublicUserResponse>> GetFollowersAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<PublicUserResponse>> GetFollowingAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
}

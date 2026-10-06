using Sunset.Application.Common;
using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface IFollowRepository
{
    Task<Follow?> GetAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken = default);
    Task AddAsync(Follow follow, CancellationToken cancellationToken = default);
    Task RemoveAsync(Follow follow, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<User>> GetFollowersAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<User>> GetFollowingAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
}

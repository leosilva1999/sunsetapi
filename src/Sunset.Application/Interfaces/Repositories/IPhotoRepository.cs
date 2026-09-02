using Sunset.Application.Common;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces.Repositories;

public interface IPhotoRepository
{
    Task<Photo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Photo>> GetFeedAsync(PhotoSortOption sort, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Photo>> GetByLocationIdAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Photo>> GetByUserIdAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task AddAsync(Photo photo, CancellationToken cancellationToken = default);
    Task RemoveAsync(Photo photo, CancellationToken cancellationToken = default);

    Task<Like?> GetLikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default);
    Task AddLikeAsync(Like like, CancellationToken cancellationToken = default);
    Task RemoveLikeAsync(Like like, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> GetLikedPhotoIdsAsync(Guid userId, IEnumerable<Guid> photoIds, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<Comment>> GetCommentsAsync(Guid photoId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<Comment?> GetCommentByIdAsync(Guid commentId, CancellationToken cancellationToken = default);
    Task AddCommentAsync(Comment comment, CancellationToken cancellationToken = default);
    Task RemoveCommentAsync(Comment comment, CancellationToken cancellationToken = default);
}

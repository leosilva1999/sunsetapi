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

    // The photo's DeletedAt/DeletedByUserId are already set by the caller (Photo.SoftDelete) -
    // this just persists that change, it doesn't remove the row.
    Task SoftDeleteAsync(Photo photo, CancellationToken cancellationToken = default);

    Task<Like?> GetLikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default);
    Task AddLikeAsync(Like like, CancellationToken cancellationToken = default);
    Task RemoveLikeAsync(Like like, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> GetLikedPhotoIdsAsync(Guid userId, IEnumerable<Guid> photoIds, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<Comment>> GetCommentsAsync(Guid photoId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Comment>> GetRepliesAsync(Guid parentCommentId, string? cursor, int limit, CancellationToken cancellationToken = default);

    // Unpaginated on purpose - replies are one level deep and realistically few per comment;
    // used only to cascade-soft-delete them when their root is moderated away.
    Task<IReadOnlyList<Comment>> GetAllRepliesAsync(Guid parentCommentId, CancellationToken cancellationToken = default);
    Task<Comment?> GetCommentByIdAsync(Guid commentId, CancellationToken cancellationToken = default);
    Task AddCommentAsync(Comment comment, CancellationToken cancellationToken = default);
    Task SoftDeleteCommentAsync(Comment comment, CancellationToken cancellationToken = default);
}

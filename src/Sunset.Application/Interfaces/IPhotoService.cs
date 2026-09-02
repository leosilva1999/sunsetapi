using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface IPhotoService
{
    Task<CursorPagedResult<PhotoResponse>> GetFeedAsync(PhotoSortOption sort, string? cursor, int limit, Guid? currentUserId, CancellationToken cancellationToken = default);
    Task<PhotoResponse> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken cancellationToken = default);
    Task<PhotoResponse> CreateAsync(Guid userId, CreatePhotoRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default);
    Task LikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default);
    Task UnlikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<CommentResponse>> GetCommentsAsync(Guid photoId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CommentResponse> AddCommentAsync(Guid userId, Guid photoId, CreateCommentRequest request, CancellationToken cancellationToken = default);
    Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken = default);
}

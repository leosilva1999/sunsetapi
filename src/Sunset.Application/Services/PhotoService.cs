using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class PhotoService(IPhotoRepository photoRepository, ILocationRepository locationRepository) : IPhotoService
{
    public async Task<CursorPagedResult<PhotoResponse>> GetFeedAsync(PhotoSortOption sort, string? cursor, int limit, Guid? currentUserId, CancellationToken cancellationToken = default)
    {
        var page = await photoRepository.GetFeedAsync(sort, cursor, limit, cancellationToken);
        var likedPhotoIds = await GetLikedPhotoIdsAsync(currentUserId, page.Items, cancellationToken);
        var items = page.Items.Select(p => p.ToResponse(likedPhotoIds.Contains(p.Id))).ToList();

        return new CursorPagedResult<PhotoResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<PhotoResponse> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        var liked = currentUserId is { } userId && await photoRepository.GetLikeAsync(userId, id, cancellationToken) is not null;
        return photo.ToResponse(liked);
    }

    public async Task<PhotoResponse> CreateAsync(Guid userId, CreatePhotoRequest request, CancellationToken cancellationToken = default)
    {
        if (await locationRepository.GetByIdAsync(request.LocationId, cancellationToken) is null)
            throw new NotFoundException("Location not found.");

        var photo = new Photo(userId, request.LocationId, request.ImageUrl, request.Caption);
        await photoRepository.AddAsync(photo, cancellationToken);

        var created = await photoRepository.GetByIdAsync(photo.Id, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        return created.ToResponse();
    }

    public async Task DeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByIdAsync(photoId, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        if (photo.UserId != userId)
            throw new UnauthorizedActionException("Only the author can delete this photo.");

        await photoRepository.RemoveAsync(photo, cancellationToken);
    }

    public async Task LikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByIdAsync(photoId, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        if (await photoRepository.GetLikeAsync(userId, photoId, cancellationToken) is not null)
            return;

        photo.IncrementLikes();
        await photoRepository.AddLikeAsync(new Like(userId, photoId), cancellationToken);
    }

    public async Task UnlikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByIdAsync(photoId, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        var like = await photoRepository.GetLikeAsync(userId, photoId, cancellationToken);
        if (like is null)
            return;

        photo.DecrementLikes();
        await photoRepository.RemoveLikeAsync(like, cancellationToken);
    }

    public async Task<CursorPagedResult<CommentResponse>> GetCommentsAsync(Guid photoId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (await photoRepository.GetByIdAsync(photoId, cancellationToken) is null)
            throw new NotFoundException("Photo not found.");

        var page = await photoRepository.GetCommentsAsync(photoId, cursor, limit, cancellationToken);
        var items = page.Items.Select(c => c.ToResponse()).ToList();

        return new CursorPagedResult<CommentResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<CommentResponse> AddCommentAsync(Guid userId, Guid photoId, CreateCommentRequest request, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByIdAsync(photoId, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        if (request.ParentCommentId is { } parentId)
        {
            var parent = await photoRepository.GetCommentByIdAsync(parentId, cancellationToken);
            if (parent is null || parent.PhotoId != photoId)
                throw new NotFoundException("Parent comment not found.");
            if (parent.ParentCommentId is not null)
                throw new ConflictException("Cannot reply to a reply.");

            parent.IncrementRepliesCount();
        }

        photo.IncrementCommentsCount();

        var comment = new Comment(userId, photoId, request.Content, request.ParentCommentId);
        await photoRepository.AddCommentAsync(comment, cancellationToken);

        var created = await photoRepository.GetCommentByIdAsync(comment.Id, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");

        return created.ToResponse();
    }

    public async Task<CursorPagedResult<CommentResponse>> GetRepliesAsync(Guid commentId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var comment = await photoRepository.GetCommentByIdAsync(commentId, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");

        if (comment.ParentCommentId is not null)
            throw new NotFoundException("Comment not found.");

        var page = await photoRepository.GetRepliesAsync(commentId, cursor, limit, cancellationToken);
        var items = page.Items.Select(c => c.ToResponse()).ToList();

        return new CursorPagedResult<CommentResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken = default)
    {
        var comment = await photoRepository.GetCommentByIdAsync(commentId, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");

        if (comment.UserId != userId)
            throw new UnauthorizedActionException("Only the author can delete this comment.");

        var photo = await photoRepository.GetByIdAsync(comment.PhotoId, cancellationToken)
            ?? throw new NotFoundException("Photo not found.");

        if (comment.ParentCommentId is { } parentId)
        {
            var parent = await photoRepository.GetCommentByIdAsync(parentId, cancellationToken);
            parent?.DecrementRepliesCount();
            photo.DecrementCommentsCount();
        }
        else
        {
            // Replies are removed by the DB cascade, not through tracked entities, so the
            // photo's count has to absorb all of them here in one shot.
            photo.DecrementCommentsCount(1 + comment.RepliesCount);
        }

        await photoRepository.RemoveCommentAsync(comment, cancellationToken);
    }

    private async Task<IReadOnlySet<Guid>> GetLikedPhotoIdsAsync(Guid? currentUserId, IReadOnlyList<Photo> photos, CancellationToken cancellationToken)
    {
        if (currentUserId is not { } userId || photos.Count == 0)
            return new HashSet<Guid>();

        return await photoRepository.GetLikedPhotoIdsAsync(userId, photos.Select(p => p.Id), cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class PhotoRepository(SunsetDbContext context) : IPhotoRepository
{
    public Task<Photo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Photos
            .Include(p => p.User)
            .Include(p => p.Location)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<CursorPagedResult<Photo>> GetFeedAsync(PhotoSortOption sort, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var photos = context.Photos.Include(p => p.User).Include(p => p.Location).AsQueryable();

        if (sort == PhotoSortOption.Top)
        {
            var decoded = TopPhotoCursor.TryDecode(cursor);
            if (decoded is { } c)
            {
                photos = photos.Where(p =>
                    p.LikesCount < c.LikesCount ||
                    (p.LikesCount == c.LikesCount && p.CreatedAt < c.CreatedAt) ||
                    (p.LikesCount == c.LikesCount && p.CreatedAt == c.CreatedAt && p.Id.CompareTo(c.Id) < 0));
            }

            var items = await photos
                .OrderByDescending(p => p.LikesCount)
                .ThenByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);

            return BuildTopPage(items, limit);
        }

        photos = ApplyRecentCursor(photos, cursor);

        var recentItems = await photos
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        return BuildRecentPage(recentItems, limit);
    }

    public async Task<CursorPagedResult<Photo>> GetByLocationIdAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var photos = context.Photos
            .Include(p => p.User)
            .Include(p => p.Location)
            .Where(p => p.LocationId == locationId);

        photos = ApplyRecentCursor(photos, cursor);

        var items = await photos
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        return BuildRecentPage(items, limit);
    }

    public async Task<CursorPagedResult<Photo>> GetByUserIdAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var photos = context.Photos
            .Include(p => p.User)
            .Include(p => p.Location)
            .Where(p => p.UserId == userId);

        photos = ApplyRecentCursor(photos, cursor);

        var items = await photos
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        return BuildRecentPage(items, limit);
    }

    public async Task AddAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        await context.Photos.AddAsync(photo, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        context.Photos.Remove(photo);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Like?> GetLikeAsync(Guid userId, Guid photoId, CancellationToken cancellationToken = default) =>
        context.Likes.FirstOrDefaultAsync(l => l.UserId == userId && l.PhotoId == photoId, cancellationToken);

    public async Task AddLikeAsync(Like like, CancellationToken cancellationToken = default)
    {
        await context.Likes.AddAsync(like, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLikeAsync(Like like, CancellationToken cancellationToken = default)
    {
        context.Likes.Remove(like);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPagedResult<Comment>> GetCommentsAsync(Guid photoId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var comments = context.Comments
            .Include(c => c.User)
            .Where(c => c.PhotoId == photoId)
            .AsQueryable();

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            comments = comments.Where(x =>
                x.CreatedAt < c.CreatedAt ||
                (x.CreatedAt == c.CreatedAt && x.Id.CompareTo(c.Id) < 0));
        }

        var items = await comments
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<Comment>(page, nextCursor, hasMore);
    }

    public Task<Comment?> GetCommentByIdAsync(Guid commentId, CancellationToken cancellationToken = default) =>
        context.Comments.Include(c => c.User).FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);

    public async Task AddCommentAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        await context.Comments.AddAsync(comment, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveCommentAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        context.Comments.Remove(comment);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<Photo> ApplyRecentCursor(IQueryable<Photo> photos, string? cursor)
    {
        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is not { } c)
            return photos;

        return photos.Where(p =>
            p.CreatedAt < c.CreatedAt ||
            (p.CreatedAt == c.CreatedAt && p.Id.CompareTo(c.Id) < 0));
    }

    private static CursorPagedResult<Photo> BuildRecentPage(List<Photo> items, int limit)
    {
        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;
        return new CursorPagedResult<Photo>(page, nextCursor, hasMore);
    }

    private static CursorPagedResult<Photo> BuildTopPage(List<Photo> items, int limit)
    {
        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? TopPhotoCursor.Encode(page[^1].LikesCount, page[^1].CreatedAt, page[^1].Id) : null;
        return new CursorPagedResult<Photo>(page, nextCursor, hasMore);
    }
}

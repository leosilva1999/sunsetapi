using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class FollowRepository(SunsetDbContext context) : IFollowRepository
{
    public Task<Follow?> GetAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken = default) =>
        context.Follows.FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowingId == followingId, cancellationToken);

    public async Task AddAsync(Follow follow, CancellationToken cancellationToken = default)
    {
        await context.Follows.AddAsync(follow, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Follow follow, CancellationToken cancellationToken = default)
    {
        context.Follows.Remove(follow);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<CursorPagedResult<User>> GetFollowersAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default) =>
        GetPageAsync(context.Follows.Where(f => f.FollowingId == userId), f => f.Follower, cursor, limit, cancellationToken);

    public Task<CursorPagedResult<User>> GetFollowingAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default) =>
        GetPageAsync(context.Follows.Where(f => f.FollowerId == userId), f => f.Following, cursor, limit, cancellationToken);

    private static async Task<CursorPagedResult<User>> GetPageAsync(
        IQueryable<Follow> query,
        Func<Follow, User> selectUser,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        query = query.Include(f => f.Follower).Include(f => f.Following);

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            query = query.Where(f =>
                f.CreatedAt < c.CreatedAt ||
                (f.CreatedAt == c.CreatedAt && f.Id.CompareTo(c.Id) < 0));
        }

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<User>(page.Select(selectUser).ToList(), nextCursor, hasMore);
    }
}

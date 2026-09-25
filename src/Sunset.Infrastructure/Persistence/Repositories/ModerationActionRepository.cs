using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class ModerationActionRepository(SunsetDbContext context) : IModerationActionRepository
{
    public async Task AddAsync(ModerationAction action, CancellationToken cancellationToken = default)
    {
        await context.ModerationActions.AddAsync(action, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPagedResult<ModerationAction>> GetAllAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var actions = context.ModerationActions.Include(a => a.Moderator).AsQueryable();

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            actions = actions.Where(a =>
                a.CreatedAt < c.CreatedAt ||
                (a.CreatedAt == c.CreatedAt && a.Id.CompareTo(c.Id) < 0));
        }

        var items = await actions
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<ModerationAction>(page, nextCursor, hasMore);
    }
}

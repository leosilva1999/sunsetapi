using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class NotificationRepository(SunsetDbContext context) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await context.Notifications.AddAsync(notification, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<CursorPagedResult<Notification>> GetByUserIdAsync(Guid recipientUserId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var notifications = context.Notifications
            .Include(n => n.Actor)
            .Where(n => n.RecipientUserId == recipientUserId)
            .AsQueryable();

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            notifications = notifications.Where(n =>
                n.CreatedAt < c.CreatedAt ||
                (n.CreatedAt == c.CreatedAt && n.Id.CompareTo(c.Id) < 0));
        }

        var items = await notifications
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<Notification>(page, nextCursor, hasMore);
    }

    public Task<int> GetUnreadCountAsync(Guid recipientUserId, CancellationToken cancellationToken = default) =>
        context.Notifications.CountAsync(n => n.RecipientUserId == recipientUserId && n.ReadAt == null, cancellationToken);

    public async Task MarkAllReadAsync(Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        var unread = await context.Notifications
            .Where(n => n.RecipientUserId == recipientUserId && n.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
            notification.MarkRead();

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class AnnouncementRepository(SunsetDbContext context) : IAnnouncementRepository
{
    private const string RemovedAccountSuffix = "@sunset.invalid";

    public async Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default)
    {
        await context.Announcements.AddAsync(announcement, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Announcement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Announcements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<CursorPagedResult<Announcement>> ListAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var announcements = context.Announcements.AsNoTracking().AsQueryable();
        if (CreatedAtCursor.TryDecode(cursor) is { } c)
            announcements = announcements.Where(a => a.CreatedAt < c.CreatedAt || (a.CreatedAt == c.CreatedAt && a.Id.CompareTo(c.Id) < 0));

        var items = await announcements.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();

        return new CursorPagedResult<Announcement>(page, hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null, hasMore);
    }

    // Active accounts that fit the audience. A promotion also needs the person's consent and at most one promotion a day
    // (the announcement being delivered is not counted against itself).
    private IQueryable<User> Eligible(AnnouncementAudienceSpec spec, Guid? announcementId)
    {
        var now = DateTime.UtcNow;
        var users = context.Users.AsNoTracking()
            .Where(u => (u.BannedAt == null || (u.BannedUntil != null && u.BannedUntil <= now)) && !u.Email.EndsWith(RemovedAccountSuffix));

        var ids = spec.UserIds.ToList();
        users = spec.Type switch
        {
            AnnouncementAudience.Users => users.Where(u => ids.Contains(u.Id)),
            AnnouncementAudience.PushEnabled => users.Where(u => context.PushDevices.Any(d => d.UserId == u.Id)),
            _ => users,
        };

        if (spec.Category == AnnouncementCategory.Promotional)
        {
            var dayAgo = now.AddHours(-24);
            users = users.Where(u =>
                context.NotificationSettings.Any(s => s.UserId == u.Id && s.PromotionalEnabled)
                && !context.Notifications.Any(n => n.RecipientUserId == u.Id && n.AnnouncementId != null && n.AnnouncementId != announcementId
                                                   && n.Announcement!.Category == AnnouncementCategory.Promotional && n.CreatedAt > dayAgo));
        }

        return users;
    }

    public Task<int> CountRecipientsAsync(AnnouncementAudienceSpec spec, CancellationToken cancellationToken = default) =>
        Eligible(spec, null).CountAsync(cancellationToken);

    public Task<int> CountMassSinceAsync(DateTime since, CancellationToken cancellationToken = default) =>
        context.Announcements.CountAsync(a => a.Audience != AnnouncementAudience.Users && a.CreatedAt >= since, cancellationToken);

    public Task<Announcement?> GetNextToDeliverAsync(CancellationToken cancellationToken = default) =>
        context.Announcements
            .Where(a => a.Status == AnnouncementStatus.Pending || a.Status == AnnouncementStatus.Sending)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetNextRecipientIdsAsync(Announcement announcement, int take, CancellationToken cancellationToken = default)
    {
        var spec = new AnnouncementAudienceSpec(announcement.Audience, announcement.Category, announcement.AudienceUserIds);
        var id = announcement.Id;

        return await Eligible(spec, id)
            .Where(u => !context.Notifications.Any(n => n.AnnouncementId == id && n.RecipientUserId == u.Id))
            .OrderBy(u => u.Id)
            .Take(take)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid UserId, Guid NotificationId)>> CreateNotificationsAsync(
        Announcement announcement, IReadOnlyCollection<Guid> recipientIds, CancellationToken cancellationToken = default)
    {
        var notifications = recipientIds.Select(id => Notification.ForAnnouncement(id, announcement.CreatedByUserId, announcement.Id)).ToList();

        await context.Notifications.AddRangeAsync(notifications, cancellationToken);
        announcement.AddCreated(notifications.Count);
        await context.SaveChangesAsync(cancellationToken);

        return notifications.Select(n => (n.RecipientUserId, n.Id)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetReadCountsAsync(IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default) =>
        await context.Notifications.AsNoTracking()
            .Where(n => n.AnnouncementId != null && announcementIds.Contains(n.AnnouncementId.Value) && n.ReadAt != null)
            .GroupBy(n => n.AnnouncementId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

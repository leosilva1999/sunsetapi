using Sunset.Application.Common;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces.Repositories;

/// <param name="UserIds">For the Users audience.</param>
public sealed record AnnouncementAudienceSpec(AnnouncementAudience Type, AnnouncementCategory Category, IReadOnlyCollection<Guid> UserIds);

public interface IAnnouncementRepository
{
    Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default);
    Task<Announcement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<CursorPagedResult<Announcement>> ListAsync(string? cursor, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many people the audience reaches right now: active accounts (not banned, not removed) that fit the audience and,
    /// for a promotional announcement, have opted in and have not had another promotion in the last 24 hours.
    /// </summary>
    Task<int> CountRecipientsAsync(AnnouncementAudienceSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Announcements to the whole base (anything but a list of accounts) created since this moment.</summary>
    Task<int> CountMassSinceAsync(DateTime since, CancellationToken cancellationToken = default);

    /// <summary>The oldest announcement still being delivered (tracked), or null.</summary>
    Task<Announcement?> GetNextToDeliverAsync(CancellationToken cancellationToken = default);

    /// <summary>The next recipients of an announcement that have no notification for it yet, by account id. Safe to call again after a crash.</summary>
    Task<IReadOnlyList<Guid>> GetNextRecipientIdsAsync(Announcement announcement, int take, CancellationToken cancellationToken = default);

    /// <summary>Creates one notification per recipient and counts them on the announcement, in one save.</summary>
    Task<IReadOnlyList<(Guid UserId, Guid NotificationId)>> CreateNotificationsAsync(Announcement announcement, IReadOnlyCollection<Guid> recipientIds, CancellationToken cancellationToken = default);

    /// <summary>How many notifications of each announcement have been read.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetReadCountsAsync(IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

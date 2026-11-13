using Sunset.Application.Common;
using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Notification>> GetByUserIdAsync(Guid recipientUserId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid recipientUserId, CancellationToken cancellationToken = default);

    /// <summary>Unread counts of several people in one round trip; people with none are absent.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetUnreadCountsAsync(IReadOnlyCollection<Guid> recipientUserIds, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

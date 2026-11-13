using Sunset.Application.Push;

namespace Sunset.Application.Interfaces;

/// <summary>The work behind the push queue: decide who gets what, send it, and keep the books on tokens and receipts.</summary>
public interface IPushDispatchService
{
    /// <summary>
    /// Sends the pushes for these notifications. Skips recipients who are banned, removed, without devices, or past
    /// the hourly limit (their notification stays in-app).
    /// </summary>
    Task DispatchAsync(IReadOnlyCollection<PushRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the groups whose window has ended ("Ana e mais 3 curtiram sua foto"). Called every few seconds by the
    /// background worker.
    /// </summary>
    Task FlushDueGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a test push to every device of the person, straight away (no queue, no grouping, no preferences) - to check
    /// the whole chain. Raises a conflict when push is switched off on the server.
    /// </summary>
    Task<PushTestResult> SendTestAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a freshly delivered announcement to the recipients' phones (the notifications already exist). Skips people who
    /// switched pushes off. Not grouped and outside the hourly cap of social pushes.
    /// </summary>
    Task DispatchAnnouncementAsync(Domain.Entities.Announcement announcement, IReadOnlyCollection<(Guid UserId, Guid NotificationId)> recipients, CancellationToken cancellationToken = default);

    /// <summary>Reads the receipts of pushes sent at least 15 minutes ago and removes the tokens the provider reports dead.</summary>
    Task ProcessReceiptsAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets devices not seen for the retention period.</summary>
    Task<int> DeleteStaleDevicesAsync(CancellationToken cancellationToken = default);
}

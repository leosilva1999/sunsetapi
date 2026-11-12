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

    /// <summary>Reads the receipts of pushes sent at least 15 minutes ago and removes the tokens the provider reports dead.</summary>
    Task ProcessReceiptsAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets devices not seen for the retention period.</summary>
    Task<int> DeleteStaleDevicesAsync(CancellationToken cancellationToken = default);
}

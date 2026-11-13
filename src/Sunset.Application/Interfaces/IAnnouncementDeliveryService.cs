namespace Sunset.Application.Interfaces;

/// <summary>The work behind the delivery worker: turns saved announcements into notifications (and pushes) in batches.</summary>
public interface IAnnouncementDeliveryService
{
    /// <summary>
    /// Delivers the oldest pending announcement, batch by batch, until every recipient has their notification, then marks it sent.
    /// Resumable: after a restart it picks up with the recipients that do not have one yet.
    /// </summary>
    /// <returns>True when it found something to deliver.</returns>
    Task<bool> DeliverNextAsync(CancellationToken cancellationToken = default);
}

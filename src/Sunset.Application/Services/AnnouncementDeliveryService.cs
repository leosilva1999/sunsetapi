using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;

namespace Sunset.Application.Services;

public class AnnouncementDeliveryService(
    IAnnouncementRepository announcementRepository,
    IPushDispatchService pushDispatchService,
    PushSettings settings) : IAnnouncementDeliveryService
{
    public async Task<bool> DeliverNextAsync(CancellationToken cancellationToken = default)
    {
        var announcement = await announcementRepository.GetNextToDeliverAsync(cancellationToken);
        if (announcement is null)
            return false;

        announcement.MarkSending();
        await announcementRepository.SaveChangesAsync(cancellationToken);

        var batchSize = Math.Max(1, settings.AnnouncementBatchSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            // People without a notification for this announcement yet: the same query resumes after a restart.
            var recipients = await announcementRepository.GetNextRecipientIdsAsync(announcement, batchSize, cancellationToken);
            if (recipients.Count == 0)
            {
                announcement.Complete(DateTime.UtcNow);
                await announcementRepository.SaveChangesAsync(cancellationToken);
                break;
            }

            var created = await announcementRepository.CreateNotificationsAsync(announcement, recipients, cancellationToken);

            // The notifications are saved first (at-least-once in the app); the push is best effort.
            await pushDispatchService.DispatchAnnouncementAsync(announcement, created, cancellationToken);
        }

        return true;
    }
}

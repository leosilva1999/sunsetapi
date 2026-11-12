using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;
using Sunset.Domain.Entities;

namespace Sunset.Application.Services;

public class PushDispatchService(
    IPushDeviceRepository deviceRepository,
    IPushTicketRepository ticketRepository,
    IUserRepository userRepository,
    INotificationRepository notificationRepository,
    IPushSender sender,
    PushThrottle throttle,
    PushSettings settings) : IPushDispatchService
{
    /// <summary>How long after sending the provider has its receipts ready.</summary>
    public static readonly TimeSpan ReceiptDelay = TimeSpan.FromMinutes(15);

    /// <summary>The provider keeps receipts for a day; a ticket older than that will never be answered.</summary>
    public static readonly TimeSpan ReceiptExpiry = TimeSpan.FromHours(24);

    private const int ReceiptBatch = 1000;

    public async Task DispatchAsync(IReadOnlyCollection<PushRequest> requests, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || requests.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var messages = new List<PushMessage>();
        var actorNames = new Dictionary<Guid, string>();

        foreach (var group in requests.GroupBy(r => r.RecipientUserId))
        {
            var recipient = await userRepository.GetByIdAsync(group.Key, cancellationToken);
            if (recipient is null || recipient.IsBanned(now) || recipient.Email.EndsWith("@sunset.invalid", StringComparison.Ordinal))
                continue;

            var devices = await deviceRepository.ListByUserAsync(group.Key, cancellationToken);
            if (devices.Count == 0)
                continue;

            var unread = await notificationRepository.GetUnreadCountAsync(group.Key, cancellationToken);
            foreach (var request in group)
            {
                // Over the hourly limit: the notification is still in the app, only the phone stays quiet.
                if (!throttle.TryAcquire(group.Key, now))
                    break;

                if (!actorNames.TryGetValue(request.ActorUserId, out var actorName))
                {
                    var actor = await userRepository.GetByIdAsync(request.ActorUserId, cancellationToken);
                    actorNames[request.ActorUserId] = actorName = actor?.Name ?? "Alguém";
                }

                messages.AddRange(devices.Select(d => PushMessageComposer.Compose(d.Token, request, actorName, unread)));
            }
        }

        if (messages.Count == 0)
            return;

        var results = await sender.SendAsync(messages, cancellationToken);

        var tickets = new List<PushTicket>();
        for (var i = 0; i < messages.Count && i < results.Count; i++)
        {
            var result = results[i];
            if (result.TicketId is not null)
                tickets.Add(new PushTicket(result.TicketId, messages[i].To));
            else if (result.Failure == PushFailure.DeviceNotRegistered)
                await deviceRepository.DeleteByTokenAsync(messages[i].To, cancellationToken);
        }

        if (tickets.Count > 0)
            await ticketRepository.AddRangeAsync(tickets, cancellationToken);
    }

    public async Task ProcessReceiptsAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return;

        var now = DateTime.UtcNow;
        var due = await ticketRepository.GetDueAsync(now - ReceiptDelay, ReceiptBatch, cancellationToken);
        if (due.Count == 0)
            return;

        var receipts = await sender.GetReceiptsAsync(due.Select(t => t.ProviderTicketId).ToList(), cancellationToken);

        var finished = new List<Guid>();
        foreach (var ticket in due)
        {
            if (receipts.TryGetValue(ticket.ProviderTicketId, out var receipt))
            {
                if (!receipt.Delivered && receipt.Failure == PushFailure.DeviceNotRegistered)
                    await deviceRepository.DeleteByTokenAsync(ticket.Token, cancellationToken);
                finished.Add(ticket.Id);
            }
            else if (ticket.CreatedAt <= now - ReceiptExpiry)
            {
                finished.Add(ticket.Id);   // the provider no longer has it: nothing more to learn
            }
        }

        if (finished.Count > 0)
            await ticketRepository.DeleteAsync(finished, cancellationToken);
    }

    public Task<int> DeleteStaleDevicesAsync(CancellationToken cancellationToken = default)
    {
        throttle.Trim(DateTime.UtcNow);
        return deviceRepository.DeleteNotSeenSinceAsync(DateTime.UtcNow.AddDays(-settings.DeviceRetentionDays), cancellationToken);
    }
}

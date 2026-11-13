using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class PushDispatchService(
    IPushDeviceRepository deviceRepository,
    IPushTicketRepository ticketRepository,
    IUserRepository userRepository,
    INotificationRepository notificationRepository,
    INotificationSettingRepository settingRepository,
    IPushSender sender,
    PushThrottle throttle,
    PushGrouper grouper,
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
        var choices = await settingRepository.GetByUsersAsync(requests.Select(r => r.RecipientUserId).Distinct().ToList(), cancellationToken);

        var sendNow = new List<PushGroup>();
        foreach (var request in requests)
        {
            // Someone who turned this kind off (or all pushes) never gets it - and opens no group window either.
            if (choices.TryGetValue(request.RecipientUserId, out var choice) && !choice.AllowsPush(request.Type))
                continue;

            // The first of a burst leaves at once; the rest wait for the window to end and leave as one push.
            if (grouper.OfferSendNow(request, now))
                sendNow.Add(new PushGroup(request, request.ActorUserId, 0));
        }

        await SendGroupsAsync(sendNow, now, cancellationToken);
    }

    public async Task FlushDueGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return;

        var now = DateTime.UtcNow;
        var due = grouper.TakeDue(now);
        if (due.Count == 0)
            return;

        // The choices may have changed while the group was waiting.
        var choices = await settingRepository.GetByUsersAsync(due.Select(g => g.Latest.RecipientUserId).Distinct().ToList(), cancellationToken);
        var allowed = due.Where(g => !choices.TryGetValue(g.Latest.RecipientUserId, out var choice) || choice.AllowsPush(g.Latest.Type)).ToList();

        await SendGroupsAsync(allowed, now, cancellationToken);
    }

    public async Task<PushTestResult> SendTestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            throw new Exceptions.ConflictException("Push notifications are switched off on this server.");

        var devices = await deviceRepository.ListByUserAsync(userId, cancellationToken);
        if (devices.Count == 0)
            return new PushTestResult(0, 0, 0);

        var unread = await notificationRepository.GetUnreadCountAsync(userId, cancellationToken);
        var data = new Dictionary<string, object> { ["v"] = PushMessageComposer.PayloadVersion, ["type"] = "Test" };
        var messages = devices
            .Select(d => new PushMessage(d.Token, "Teste de notificação", "Se você viu isto, as notificações do Sunset estão funcionando.", unread, data))
            .ToList();

        var results = await sender.SendAsync(messages, cancellationToken);
        var (accepted, removed) = await BookAsync(messages, results, cancellationToken);
        return new PushTestResult(devices.Count, accepted, removed);
    }

    public async Task DispatchAnnouncementAsync(Announcement announcement, IReadOnlyCollection<(Guid UserId, Guid NotificationId)> recipients, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || recipients.Count == 0)
            return;

        var ids = recipients.Select(r => r.UserId).ToList();
        var choices = await settingRepository.GetByUsersAsync(ids, cancellationToken);
        var devices = (await deviceRepository.ListByUsersAsync(ids, cancellationToken)).ToLookup(d => d.UserId);
        var unread = await notificationRepository.GetUnreadCountsAsync(ids, cancellationToken);

        var messages = new List<PushMessage>();
        foreach (var (userId, notificationId) in recipients)
        {
            // Pushes switched off at the phone's master switch: the announcement is still in the app.
            if (choices.TryGetValue(userId, out var choice) && !choice.PushEnabled)
                continue;

            messages.AddRange(devices[userId].Select(d =>
                PushAnnouncementComposer.Compose(d.Token, announcement, notificationId, unread.GetValueOrDefault(userId))));
        }

        if (messages.Count == 0)
            return;

        var results = await sender.SendAsync(messages, cancellationToken);
        await BookAsync(messages, results, cancellationToken);
    }

    private async Task SendGroupsAsync(IReadOnlyCollection<PushGroup> groups, DateTime now, CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
            return;

        var messages = new List<PushMessage>();
        var actorNames = new Dictionary<Guid, string>();

        foreach (var byRecipient in groups.GroupBy(g => g.Latest.RecipientUserId))
        {
            var recipient = await userRepository.GetByIdAsync(byRecipient.Key, cancellationToken);
            if (recipient is null || recipient.IsBanned(now) || recipient.Email.EndsWith("@sunset.invalid", StringComparison.Ordinal))
                continue;

            var devices = await deviceRepository.ListByUserAsync(byRecipient.Key, cancellationToken);
            if (devices.Count == 0)
                continue;

            var unread = await notificationRepository.GetUnreadCountAsync(byRecipient.Key, cancellationToken);
            foreach (var group in byRecipient)
            {
                // Over the hourly limit: the notification is still in the app, only the phone stays quiet.
                if (!throttle.TryAcquire(byRecipient.Key, now))
                    break;

                if (!actorNames.TryGetValue(group.FirstActorId, out var actorName))
                {
                    var actor = await userRepository.GetByIdAsync(group.FirstActorId, cancellationToken);
                    actorNames[group.FirstActorId] = actorName = actor?.Name ?? "Alguém";
                }

                messages.AddRange(devices.Select(d => PushMessageComposer.Compose(d.Token, group.Latest, actorName, unread, group.OtherActors)));
            }
        }

        if (messages.Count == 0)
            return;

        var results = await sender.SendAsync(messages, cancellationToken);
        await BookAsync(messages, results, cancellationToken);
    }

    /// <summary>Keeps the books on what the provider answered: a ticket per accepted message, dead tokens removed at once.</summary>
    private async Task<(int Accepted, int Removed)> BookAsync(IReadOnlyList<PushMessage> messages, IReadOnlyList<PushSendResult> results, CancellationToken cancellationToken)
    {
        var tickets = new List<PushTicket>();
        var removed = 0;
        for (var i = 0; i < messages.Count && i < results.Count; i++)
        {
            var result = results[i];
            if (result.TicketId is not null)
                tickets.Add(new PushTicket(result.TicketId, messages[i].To));
            else if (result.Failure == PushFailure.DeviceNotRegistered)
                removed += await deviceRepository.DeleteByTokenAsync(messages[i].To, cancellationToken);
        }

        if (tickets.Count > 0)
            await ticketRepository.AddRangeAsync(tickets, cancellationToken);
        return (tickets.Count, removed);
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

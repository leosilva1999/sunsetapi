using Sunset.Domain.Enums;

namespace Sunset.Application.Push;

/// <summary>Settings of the push channel (section "Push"). Everything is off until <see cref="Enabled"/> is set.</summary>
public sealed class PushSettings
{
    /// <summary>Master switch. Off: registrations are still accepted, nothing is sent.</summary>
    public bool Enabled { get; set; }

    /// <summary>On: pushes are composed and written to the log instead of being sent (development).</summary>
    public bool DryRun { get; set; }

    /// <summary>Expo "enhanced push security" access token; optional but recommended. Never logged.</summary>
    public string? ExpoAccessToken { get; set; }

    public string BaseUrl { get; set; } = "https://exp.host";

    /// <summary>More pushes than this to one person within an hour stay in-app only.</summary>
    public int MaxPushesPerUserPerHour { get; set; } = 20;

    /// <summary>Registering one more device drops the least recently seen one.</summary>
    public int MaxDevicesPerUser { get; set; } = 5;

    /// <summary>A device not seen for this many days is forgotten.</summary>
    public int DeviceRetentionDays { get; set; } = 90;

    /// <summary>First wait between attempts when the provider is unreachable or busy; it doubles each time.</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// After a push goes out for (person, kind, target), further ones for the same key within this many seconds are
    /// collected and sent as one ("Ana e mais 3 curtiram sua foto"). 0 turns grouping off.
    /// </summary>
    public int GroupWindowSeconds { get; set; } = 60;

    /// <summary>How often the worker looks for announcements to deliver.</summary>
    public int AnnouncementPollSeconds { get; set; } = 5;

    /// <summary>Recipients handled per round while delivering an announcement.</summary>
    public int AnnouncementBatchSize { get; set; } = 200;
}

/// <summary>A notification that has just been saved and may deserve a push to its recipient.</summary>
public sealed record PushRequest(Guid NotificationId, Guid RecipientUserId, Guid ActorUserId, NotificationType Type, string TargetDescription);

/// <summary>One message for one device, ready for the provider.</summary>
public sealed record PushMessage(string To, string Title, string Body, int Badge, IReadOnlyDictionary<string, object> Data);

public enum PushFailure
{
    None,

    /// <summary>The token is dead (app uninstalled, token revoked): remove it.</summary>
    DeviceNotRegistered,

    /// <summary>The provider was unreachable or busy even after retries: give up on this push.</summary>
    Transient,

    /// <summary>The provider refused the message for another reason (too big, bad credentials...): do not retry.</summary>
    Rejected
}

/// <param name="TicketId">The provider's id for an accepted message; null when it was not accepted.</param>
public sealed record PushSendResult(string? TicketId, PushFailure Failure)
{
    public static PushSendResult Accepted(string ticketId) => new(ticketId, PushFailure.None);
    public static PushSendResult Failed(PushFailure failure) => new(null, failure);
}

/// <summary>What a test push reached: devices the person has, messages the provider accepted, dead tokens removed.</summary>
public sealed record PushTestResult(int Devices, int Accepted, int Removed);

/// <summary>What the provider finally said about a message it had accepted.</summary>
public sealed record PushReceipt(bool Delivered, PushFailure Failure);

using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

/// <summary>
/// One row per "someone did something you'd want to know about" event. Mirrors ModerationAction's
/// shape - TargetDescription is a free-text label ("Photo:{id}", "Comment:{id}", "User:{id}")
/// rather than a typed/polymorphic reference, since the target kind varies by NotificationType.
/// </summary>
public class Notification : BaseEntity
{
    public Guid RecipientUserId { get; private set; }
    public NotificationType Type { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string TargetDescription { get; private set; } = null!;
    public DateTime? ReadAt { get; private set; }

    /// <summary>Set when this notification delivers an announcement from the team (then <see cref="Type"/> is Announcement).</summary>
    public Guid? AnnouncementId { get; private set; }

    public Announcement? Announcement { get; private set; }

    public User Recipient { get; private set; } = null!;
    public User Actor { get; private set; } = null!;

    private Notification() { }

    public Notification(Guid recipientUserId, NotificationType type, Guid actorUserId, string targetDescription)
    {
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("RecipientUserId is required.", nameof(recipientUserId));
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(targetDescription))
            throw new ArgumentException("TargetDescription is required.", nameof(targetDescription));

        RecipientUserId = recipientUserId;
        Type = type;
        ActorUserId = actorUserId;
        TargetDescription = targetDescription;
    }

    /// <summary>The copy of an announcement that goes to one recipient. The "actor" is the staff sender and is never shown.</summary>
    public static Notification ForAnnouncement(Guid recipientUserId, Guid senderUserId, Guid announcementId)
    {
        var notification = new Notification(recipientUserId, NotificationType.Announcement, senderUserId, $"Announcement:{announcementId}")
        {
            AnnouncementId = announcementId,
        };
        return notification;
    }

    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}

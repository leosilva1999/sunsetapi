using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

/// <summary>
/// One person's choices about the push channel. The row is created the first time they save choices; no row means
/// the defaults (everything on). It only affects pushes: the notifications list in the app is always complete.
/// </summary>
public class NotificationSetting : BaseEntity
{
    public Guid UserId { get; private set; }

    /// <summary>Master switch for pushes to the phone.</summary>
    public bool PushEnabled { get; private set; } = true;

    /// <summary>Bit i set = pushes of the <see cref="NotificationType"/> with value i are off.</summary>
    public int MutedTypes { get; private set; }

    public User User { get; private set; } = null!;

    private NotificationSetting() { }

    public NotificationSetting(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        UserId = userId;
    }

    public bool AllowsPush(NotificationType type) => PushEnabled && !IsMuted(type);

    public bool IsMuted(NotificationType type) => (MutedTypes & Bit(type)) != 0;

    /// <summary>Replaces all the choices: the types listed are muted, every other type is on.</summary>
    public void Replace(bool pushEnabled, IEnumerable<NotificationType> mutedTypes)
    {
        PushEnabled = pushEnabled;
        MutedTypes = mutedTypes.Aggregate(0, (mask, type) => mask | Bit(type));
    }

    private static int Bit(NotificationType type) => 1 << (int)type;
}

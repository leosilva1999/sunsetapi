namespace Sunset.Application.DTOs.Notifications;

/// <param name="PushEnabled">Master switch for pushes to the phone.</param>
/// <param name="Types">One flag per kind of notification, keyed by its name (NewFollower, PhotoLiked, PhotoCommented, CommentReplied). All four always come back.</param>
/// <param name="PromotionalEnabled">Consent to promotional announcements from the team. Off until the person turns it on.</param>
public sealed record NotificationSettingsResponse(bool PushEnabled, IReadOnlyDictionary<string, bool> Types, bool PromotionalEnabled = false);

/// <summary>Replaces the choices. A kind left out of <paramref name="Types"/> is on; <paramref name="PromotionalEnabled"/> left out is off.</summary>
public sealed record UpdateNotificationSettingsRequest(bool PushEnabled, Dictionary<string, bool>? Types = null, bool PromotionalEnabled = false);

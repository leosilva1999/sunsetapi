namespace Sunset.Application.DTOs.Notifications;

/// <param name="PushEnabled">Master switch for pushes to the phone.</param>
/// <param name="Types">One flag per kind of notification, keyed by its name (NewFollower, PhotoLiked, PhotoCommented, CommentReplied). All four always come back.</param>
public sealed record NotificationSettingsResponse(bool PushEnabled, IReadOnlyDictionary<string, bool> Types);

/// <summary>Replaces the choices. A kind left out of <paramref name="Types"/> is on.</summary>
public sealed record UpdateNotificationSettingsRequest(bool PushEnabled, Dictionary<string, bool>? Types = null);

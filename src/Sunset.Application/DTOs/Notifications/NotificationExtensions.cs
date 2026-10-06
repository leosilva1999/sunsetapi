using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Notifications;

public static class NotificationExtensions
{
    public static NotificationResponse ToResponse(this Notification notification) =>
        new(
            notification.Id,
            notification.Type,
            notification.ActorUserId,
            notification.Actor.Name,
            notification.Actor.AvatarUrl,
            notification.TargetDescription,
            notification.ReadAt,
            notification.CreatedAt);
}

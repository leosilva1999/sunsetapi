using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Notifications;

public static class NotificationExtensions
{
    public const string TeamName = "Equipe Sunset";

    public static NotificationResponse ToResponse(this Notification notification) =>
        notification.Announcement is { } announcement
            // From the team: its content comes with it, and the staff member who sent it stays anonymous.
            ? new(
                notification.Id, notification.Type, Guid.Empty, TeamName, null, notification.TargetDescription,
                notification.ReadAt, notification.CreatedAt,
                announcement.Title, announcement.Body, announcement.Category, announcement.Link, announcement.Id)
            : new(
                notification.Id,
                notification.Type,
                notification.ActorUserId,
                notification.Actor.Name,
                notification.Actor.AvatarUrl,
                notification.TargetDescription,
                notification.ReadAt,
                notification.CreatedAt);
}

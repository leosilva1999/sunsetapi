using Sunset.Application.Common;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;

namespace Sunset.Application.Services;

public class NotificationService(INotificationRepository notificationRepository) : INotificationService
{
    public async Task<CursorPagedResult<NotificationResponse>> GetNotificationsAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var page = await notificationRepository.GetByUserIdAsync(userId, cursor, limit, cancellationToken);
        var items = page.Items.Select(n => n.ToResponse()).ToList();

        return new CursorPagedResult<NotificationResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<NotificationUnreadCountResponse> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        new(await notificationRepository.GetUnreadCountAsync(userId, cancellationToken));

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification not found.");

        if (notification.RecipientUserId != userId)
            throw new UnauthorizedActionException("Only the recipient can mark this notification as read.");

        notification.MarkRead();
        await notificationRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await notificationRepository.MarkAllReadAsync(userId, cancellationToken);
}

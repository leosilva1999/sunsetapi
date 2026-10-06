using Sunset.Application.Common;
using Sunset.Application.DTOs.Notifications;

namespace Sunset.Application.Interfaces;

public interface INotificationService
{
    Task<CursorPagedResult<NotificationResponse>> GetNotificationsAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<NotificationUnreadCountResponse> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

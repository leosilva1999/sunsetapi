using Sunset.Application.DTOs.Notifications;

namespace Sunset.Application.Interfaces;

public interface INotificationSettingsService
{
    /// <summary>The caller's choices; everything on if they never saved any.</summary>
    Task<NotificationSettingsResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the caller's choices. An unknown kind in the request is a validation error.</summary>
    Task<NotificationSettingsResponse> UpdateAsync(Guid userId, UpdateNotificationSettingsRequest request, CancellationToken cancellationToken = default);
}

using FluentValidation;
using FluentValidation.Results;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class NotificationSettingsService(INotificationSettingRepository settingRepository) : INotificationSettingsService
{
    // Announcements are not one of the kinds a person mutes: service notices follow the master switch, and promotions have
    // their own consent flag.
    private static IEnumerable<NotificationType> MutableTypes => Enum.GetValues<NotificationType>().Where(t => t != NotificationType.Announcement);

    public async Task<NotificationSettingsResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ToResponse(await settingRepository.GetByUserAsync(userId, cancellationToken));

    public async Task<NotificationSettingsResponse> UpdateAsync(Guid userId, UpdateNotificationSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var muted = new List<NotificationType>();
        foreach (var (name, enabled) in request.Types ?? [])
        {
            if (!Enum.TryParse<NotificationType>(name, ignoreCase: true, out var type) || !MutableTypes.Contains(type))
                throw new ValidationException([new ValidationFailure("types", $"Unknown notification type '{name}'. Use: {string.Join(", ", MutableTypes)}.")]);
            if (!enabled)
                muted.Add(type);
        }

        var setting = await settingRepository.GetByUserAsync(userId, cancellationToken);
        if (setting is null)
        {
            setting = new NotificationSetting(userId);
            await settingRepository.AddAsync(setting, cancellationToken);
        }

        setting.Replace(request.PushEnabled, muted, request.PromotionalEnabled);
        await settingRepository.SaveChangesAsync(cancellationToken);
        return ToResponse(setting);
    }

    private static NotificationSettingsResponse ToResponse(NotificationSetting? setting) =>
        new(setting?.PushEnabled ?? true,
            MutableTypes.ToDictionary(t => t.ToString(), t => setting is null || !setting.IsMuted(t)),
            setting?.PromotionalEnabled ?? false);
}

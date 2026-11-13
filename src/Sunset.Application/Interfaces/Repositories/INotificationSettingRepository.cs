using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface INotificationSettingRepository
{
    Task<NotificationSetting?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The saved choices of these people; whoever never saved any is simply absent (the defaults apply).</summary>
    Task<IReadOnlyDictionary<Guid, NotificationSetting>> GetByUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    Task AddAsync(NotificationSetting setting, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

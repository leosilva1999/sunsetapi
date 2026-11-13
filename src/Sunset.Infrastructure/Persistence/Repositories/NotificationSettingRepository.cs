using Microsoft.EntityFrameworkCore;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class NotificationSettingRepository(SunsetDbContext context) : INotificationSettingRepository
{
    public Task<NotificationSetting?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.NotificationSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, NotificationSetting>> GetByUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
        await context.NotificationSettings.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, cancellationToken);

    public async Task AddAsync(NotificationSetting setting, CancellationToken cancellationToken = default) =>
        await context.NotificationSettings.AddAsync(setting, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

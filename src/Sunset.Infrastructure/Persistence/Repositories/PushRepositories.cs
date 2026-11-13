using Microsoft.EntityFrameworkCore;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class PushDeviceRepository(SunsetDbContext context) : IPushDeviceRepository
{
    public Task<PushDevice?> GetByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        context.PushDevices.FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

    public Task<PushDevice?> GetByInstallAsync(Guid userId, Guid installId, CancellationToken cancellationToken = default) =>
        context.PushDevices.FirstOrDefaultAsync(d => d.UserId == userId && d.InstallId == installId, cancellationToken);

    public async Task<IReadOnlyList<PushDevice>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.PushDevices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt).ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PushDevice>> ListByUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
        await context.PushDevices.AsNoTracking()
            .Where(d => userIds.Contains(d.UserId))
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PushDevice device, CancellationToken cancellationToken = default) =>
        await context.PushDevices.AddAsync(device, cancellationToken);

    public void Remove(PushDevice device) => context.PushDevices.Remove(device);

    public Task<int> DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.PushDevices.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        context.PushDevices.Where(d => d.Token == token).ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteNotSeenSinceAsync(DateTime cutoff, CancellationToken cancellationToken = default) =>
        context.PushDevices.Where(d => d.LastSeenAt < cutoff).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

public class PushTicketRepository(SunsetDbContext context) : IPushTicketRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<PushTicket> tickets, CancellationToken cancellationToken = default)
    {
        await context.PushTickets.AddRangeAsync(tickets, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PushTicket>> GetDueAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default) =>
        await context.PushTickets.AsNoTracking()
            .Where(t => t.CreatedAt <= createdBefore)
            .OrderBy(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.PushTickets.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(cancellationToken);
}

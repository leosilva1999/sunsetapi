using Microsoft.Extensions.Caching.Memory;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;

namespace Sunset.Infrastructure.Persistence;

public class BanStatusService(IUserRepository userRepository, IMemoryCache cache) : IBanStatusService
{
    // Short on purpose: bounds the DB reads (one per user per window) while a ban made on
    // another instance still lands within this window. Same-instance bans call Invalidate.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private sealed record Snapshot(DateTime? BannedAt, DateTime? BannedUntil, string? Reason);

    public async Task<ActiveBan?> GetActiveBanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var snapshot = await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var user = await userRepository.GetByIdAsync(userId, cancellationToken);
            return new Snapshot(user?.BannedAt, user?.BannedUntil, user?.BanReason);
        });

        // Evaluated on every read, so a temporary ban lifts at BannedUntil even while cached.
        var active = snapshot!.BannedAt is not null && (snapshot.BannedUntil is null || snapshot.BannedUntil > DateTime.UtcNow);
        return active ? new ActiveBan(snapshot.Reason ?? string.Empty, snapshot.BannedUntil) : null;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"ban:{userId:N}";
}

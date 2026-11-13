using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface IPushDeviceRepository
{
    Task<PushDevice?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>The user's device for this installation of the app, if the app told us its install id.</summary>
    Task<PushDevice?> GetByInstallAsync(Guid userId, Guid installId, CancellationToken cancellationToken = default);

    /// <summary>The devices of several users in one round trip.</summary>
    Task<IReadOnlyList<PushDevice>> ListByUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>The user's devices, most recently seen first.</summary>
    Task<IReadOnlyList<PushDevice>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(PushDevice device, CancellationToken cancellationToken = default);
    void Remove(PushDevice device);

    /// <summary>Removes every device of the user (account deleted or banned).</summary>
    Task<int> DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Removes the device(s) holding this token (the provider said it is dead).</summary>
    Task<int> DeleteByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Removes devices not seen since <paramref name="cutoff"/>.</summary>
    Task<int> DeleteNotSeenSinceAsync(DateTime cutoff, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

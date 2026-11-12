using Sunset.Application.DTOs.Devices;

namespace Sunset.Application.Interfaces;

public interface IPushDeviceService
{
    /// <summary>
    /// Registers or refreshes the caller's device. Idempotent. A token already held by another account moves to the
    /// caller; a known install id with a new token updates that device; beyond the per-user limit the least
    /// recently seen device is dropped.
    /// </summary>
    Task RegisterAsync(Guid userId, RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes the caller's device by token or install id. Idempotent: an unknown device is not an error.</summary>
    Task RemoveAsync(Guid userId, RemoveDeviceRequest request, CancellationToken cancellationToken = default);
}

using Sunset.Application.DTOs.Devices;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;
using Sunset.Domain.Entities;

namespace Sunset.Application.Services;

public class PushDeviceService(IPushDeviceRepository deviceRepository, PushSettings settings) : IPushDeviceService
{
    public async Task RegisterAsync(Guid userId, RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.Token.Trim();
        var appVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim();
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? null : request.Locale.Trim();

        var device = await deviceRepository.GetByTokenAsync(token, cancellationToken);
        if (device is not null)
        {
            // Same token: refresh it. If it was another account's, it is now the caller's - a token never receives
            // the pushes of two accounts.
            if (device.UserId != userId)
                device.MoveTo(userId);
            device.Refresh(token, request.Platform, request.InstallId, appVersion, locale);
        }
        else if (request.InstallId is { } installId && await deviceRepository.GetByInstallAsync(userId, installId, cancellationToken) is { } sameInstall)
        {
            // Same installation, new token (the OS rotated it): update the row instead of leaving a dead one behind.
            sameInstall.Refresh(token, request.Platform, installId, appVersion, locale);
        }
        else
        {
            await deviceRepository.AddAsync(new PushDevice(userId, token, request.Platform, request.InstallId, appVersion, locale), cancellationToken);
        }

        await deviceRepository.SaveChangesAsync(cancellationToken);
        await DropOldestBeyondTheLimitAsync(userId, cancellationToken);
    }

    public async Task RemoveAsync(Guid userId, RemoveDeviceRequest request, CancellationToken cancellationToken = default)
    {
        PushDevice? device = null;
        if (!string.IsNullOrWhiteSpace(request.Token))
            device = await deviceRepository.GetByTokenAsync(request.Token.Trim(), cancellationToken);
        if (device is null && request.InstallId is { } installId)
            device = await deviceRepository.GetByInstallAsync(userId, installId, cancellationToken);

        // Only the caller's own device is removed; anyone else's token (or an unknown one) is a quiet no-op.
        if (device is null || device.UserId != userId)
            return;

        deviceRepository.Remove(device);
        await deviceRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task DropOldestBeyondTheLimitAsync(Guid userId, CancellationToken cancellationToken)
    {
        var devices = await deviceRepository.ListByUserAsync(userId, cancellationToken);   // most recently seen first
        if (devices.Count <= settings.MaxDevicesPerUser)
            return;

        foreach (var extra in devices.Skip(settings.MaxDevicesPerUser))
            deviceRepository.Remove(extra);
        await deviceRepository.SaveChangesAsync(cancellationToken);
    }
}

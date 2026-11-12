using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sunset.Application.Interfaces;
using Sunset.Application.Push;

namespace Sunset.Infrastructure.Push;

/// <summary>
/// Drains the push queue. After the first request it waits a moment and takes what else has piled up, so a burst
/// (a photo going viral) is handled as one batch with one lookup per recipient.
/// </summary>
public sealed class PushNotifierWorker(PushNotifier notifier, IServiceScopeFactory scopeFactory, ILogger<PushNotifierWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);
    private const int MaxBatch = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = notifier.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(CoalesceWindow, stoppingToken);

                var batch = new List<PushRequest>();
                while (batch.Count < MaxBatch && reader.TryRead(out var item))
                    batch.Add(item);

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IPushDispatchService>().DispatchAsync(batch, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The notifications are saved and visible in the app; only the push is lost.
                    logger.LogWarning(ex, "Push batch of {Count} failed", batch.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}

/// <summary>Housekeeping: reads delivery receipts every few minutes (dead tokens go), forgets unused devices once a day.</summary>
public sealed class PushMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<PushMaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ReceiptsEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleEvery = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextStale = DateTime.UtcNow.AddMinutes(10);   // not right at boot, while the app is still warming up
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ReceiptsEvery, stoppingToken);

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var dispatch = scope.ServiceProvider.GetRequiredService<IPushDispatchService>();

                    await dispatch.ProcessReceiptsAsync(stoppingToken);

                    if (DateTime.UtcNow >= nextStale)
                    {
                        var removed = await dispatch.DeleteStaleDevicesAsync(stoppingToken);
                        nextStale = DateTime.UtcNow + StaleEvery;
                        if (removed > 0)
                            logger.LogInformation("Forgot {Count} push device(s) not seen for a long time", removed);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Push maintenance failed; it will run again");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}

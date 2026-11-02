using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sunset.Infrastructure.SiteNotifications;

/// <summary>
/// Drains the queue. After the first item it waits a moment and takes whatever else has piled up, so a burst of
/// changes (a merge touches many things) becomes one call to the site instead of dozens.
/// </summary>
public sealed class SiteNotifierWorker(SiteNotifier notifier, SiteNotificationSender sender, ILogger<SiteNotifierWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);
    private const int MaxBatch = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = notifier.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(CoalesceWindow, stoppingToken);

                var batch = new List<SiteNotification>();
                while (batch.Count < MaxBatch && reader.TryRead(out var item))
                    batch.Add(item);

                try
                {
                    await sender.SendAsync(batch, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Site notification batch failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; whatever is still queued is dropped (the sites' own revalidation window covers it)
        }
    }
}

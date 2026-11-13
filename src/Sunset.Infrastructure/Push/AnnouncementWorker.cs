using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sunset.Application.Interfaces;
using Sunset.Application.Push;

namespace Sunset.Infrastructure.Push;

/// <summary>
/// Delivers saved announcements: every few seconds it takes the oldest unfinished one and creates the recipients'
/// notifications (and pushes) in batches. The state lives in the database, so a restart in the middle of a delivery just
/// continues with the people who do not have their notification yet.
/// </summary>
public sealed class AnnouncementWorker(IServiceScopeFactory scopeFactory, PushSettings settings, ILogger<AnnouncementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settings.AnnouncementPollSeconds)), stoppingToken);

                try
                {
                    // Keep going while there is something to deliver, so a queue of announcements does not wait a poll between each.
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        if (!await scope.ServiceProvider.GetRequiredService<IAnnouncementDeliveryService>().DeliverNextAsync(stoppingToken))
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Delivering an announcement failed; it will be tried again");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; an unfinished announcement continues after the next start
        }
    }
}

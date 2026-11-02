using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.SiteNotifications;

/// <summary>
/// Delivers a batch of notifications: <c>POST {Frontend:BaseUrl}/api/revalidate</c> with
/// <c>{ "paths": [...], "tags": [...] }</c> and the <c>X-Revalidate-Secret</c> header, then the IndexNow ping for
/// pages that are new. Each delivery is tried twice; a final failure is logged (never the secret) and dropped.
/// </summary>
public sealed class SiteNotificationSender(
    IHttpClientFactory httpClientFactory,
    IOptions<RevalidationOptions> siteOptions,
    IOptions<IndexNowOptions> indexNowOptions,
    ILogger<SiteNotificationSender> logger)
{
    public const string HttpClientName = "site-notifications";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task SendAsync(IReadOnlyCollection<SiteNotification> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        var tags = batch.Aggregate(ResponseCacheTag.None, (all, n) => all | n.Tags);
        var paths = batch.Where(n => n.Change is not null).SelectMany(n => n.Change!.Paths).Distinct(StringComparer.Ordinal).ToList();
        var indexPaths = batch.Where(n => n.Change is { Index: true }).SelectMany(n => n.Change!.Paths).Distinct(StringComparer.Ordinal).ToList();

        await RevalidateAsync(tags, paths, cancellationToken);
        await IndexNowAsync(indexPaths, cancellationToken);
    }

    public static IReadOnlyList<string> TagNames(ResponseCacheTag tags)
    {
        var names = new List<string>();
        if (tags.HasFlag(ResponseCacheTag.Locations)) names.Add("locations");
        if (tags.HasFlag(ResponseCacheTag.Photos)) names.Add("photos");
        if (tags.HasFlag(ResponseCacheTag.Cities)) names.Add("cities");
        return names;
    }

    private async Task RevalidateAsync(ResponseCacheTag tags, List<string> paths, CancellationToken cancellationToken)
    {
        var site = siteOptions.Value;
        if (string.IsNullOrWhiteSpace(site.RevalidateSecret) || string.IsNullOrWhiteSpace(site.BaseUrl))
            return;

        var url = site.BaseUrl.TrimEnd('/') + "/api/revalidate";
        var body = new { paths, tags = TagNames(tags) };

        await WithRetryAsync("revalidate", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-Revalidate-Secret", site.RevalidateSecret);
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }

    private async Task IndexNowAsync(List<string> paths, CancellationToken cancellationToken)
    {
        var key = indexNowOptions.Value.Key;
        var baseUrl = siteOptions.Value.BaseUrl;
        if (paths.Count == 0 || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(baseUrl))
            return;

        var root = baseUrl.TrimEnd('/');
        var body = new
        {
            host = new Uri(root).Host,
            key,
            keyLocation = $"{root}/{key}.txt",
            urlList = paths.Select(p => root + p).ToList(),
        };

        await WithRetryAsync("indexnow", async () =>
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .PostAsJsonAsync(indexNowOptions.Value.Endpoint, body, cancellationToken);
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }

    private async Task WithRetryAsync(string what, Func<Task> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt == 2)
                {
                    logger.LogWarning(ex, "Could not deliver the {What} notification to the site; giving up", what);
                    return;
                }

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}

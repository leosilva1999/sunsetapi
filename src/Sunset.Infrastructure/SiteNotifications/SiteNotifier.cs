using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.SiteNotifications;

/// <summary>One thing to tell the site: the groups that changed and, when known, the pages.</summary>
public sealed record SiteNotification(ResponseCacheTag Tags, SiteChange? Change);

/// <summary>
/// The in-memory queue behind <see cref="ISiteNotifier"/>. Bounded, dropping the oldest entry when full (the
/// next change repeats the same tags anyway), and a no-op when neither the site nor IndexNow is configured, so
/// development and the test suite pay nothing.
/// </summary>
public sealed class SiteNotifier : ISiteNotifier
{
    private readonly Channel<SiteNotification> _channel = Channel.CreateBounded<SiteNotification>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly bool _enabled;

    public SiteNotifier(IOptions<RevalidationOptions> site, IOptions<IndexNowOptions> indexNow)
    {
        _enabled = !string.IsNullOrWhiteSpace(site.Value.RevalidateSecret) && !string.IsNullOrWhiteSpace(site.Value.BaseUrl)
                   || !string.IsNullOrWhiteSpace(indexNow.Value.Key) && !string.IsNullOrWhiteSpace(site.Value.BaseUrl);
    }

    public ChannelReader<SiteNotification> Reader => _channel.Reader;

    public void Enqueue(ResponseCacheTag tags, SiteChange? change = null)
    {
        if (!_enabled || tags == ResponseCacheTag.None && change is null)
            return;

        _channel.Writer.TryWrite(new SiteNotification(tags, change));
    }
}

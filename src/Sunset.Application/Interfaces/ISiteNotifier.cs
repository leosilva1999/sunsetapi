namespace Sunset.Application.Interfaces;

/// <summary>
/// Tells the public site (and search engines) that something it shows has changed: the site revalidates its
/// cached pages and IndexNow hears about new URLs. Fire-and-forget by design: <see cref="Enqueue"/> never waits,
/// never throws and a failed delivery is only logged, so it can never break the request that made the change.
/// </summary>
public interface ISiteNotifier
{
    /// <param name="tags">Which groups of public data changed (sent as the tags "locations", "photos", "cities").</param>
    /// <param name="change">The exact pages, when known; null when only the groups are known.</param>
    void Enqueue(ResponseCacheTag tags, SiteChange? change = null);
}

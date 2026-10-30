using Microsoft.AspNetCore.OutputCaching;
using Sunset.Application.Interfaces;

namespace Sunset.API.Caching;

/// <summary>
/// The cache of PUBLIC (anonymous) read responses. In-memory and per instance, which is right for a single
/// instance; with several, an eviction only reaches the instance that served the write and the time-to-live
/// is what keeps the others honest.
/// </summary>
public static class PublicCaching
{
    public const string CitiesPolicy = "Cities";
    public const string LocationsPolicy = "Locations";
    public const string PhotosPolicy = "Photos";
    public const string SitemapPolicy = "Sitemap";

    public const string CitiesTag = "cities";
    public const string LocationsTag = "locations";
    public const string PhotosTag = "photos";

    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    // Browsers, the site's server and a CDN may reuse a response for the same time and serve it a bit longer
    // while they refresh it in the background.
    public const string CacheControl = "public, max-age=60, stale-while-revalidate=300";

    /// <summary>In the Testing environment the cache stays OFF unless a request opts in with this header.</summary>
    public const string TestOptInHeader = "X-Test-Cache";

    public static IServiceCollection AddPublicOutputCache(this IServiceCollection services, bool optInOnly)
    {
        services.AddOutputCache(options =>
        {
            // Responses depend on the query string (filters, cursors) and, through CORS, on the Origin header
            // (the cached response carries Access-Control-Allow-Origin of the origin that populated it).
            // Requests with an Authorization header are never cached nor served from the cache (the default
            // policy), so per-user fields such as likedByCurrentUser can't leak between visitors.
            void Add(string name, params string[] tags) => AddWithTtl(name, TimeToLive, tags);
            void AddWithTtl(string name, TimeSpan ttl, params string[] tags) => options.AddPolicy(name, builder =>
            {
                builder.Expire(ttl).SetVaryByQuery("*").SetVaryByHeader("Origin").Tag(tags);
                if (optInOnly)
                    builder.AddPolicy<TestOptInPolicy>();
            });

            // A city page shows counts, covers and rankings that move with locations, photos and ratings.
            Add(CitiesPolicy, CitiesTag, LocationsTag, PhotosTag);
            Add(LocationsPolicy, LocationsTag, PhotosTag);
            Add(PhotosPolicy, PhotosTag);
            // Walked by crawlers, heavy to build and fine to be a bit old; writes still evict it.
            AddWithTtl(SitemapPolicy, TimeSpan.FromHours(1), CitiesTag, LocationsTag, PhotosTag);
        });

        services.AddScoped<IResponseCacheInvalidator, OutputCacheInvalidator>();
        return services;
    }
}

/// <summary>Evicts by tag. A failure is logged and swallowed: the time-to-live bounds the staleness anyway.</summary>
public sealed class OutputCacheInvalidator(IOutputCacheStore store, ILogger<OutputCacheInvalidator> logger) : IResponseCacheInvalidator
{
    public async Task InvalidateAsync(ResponseCacheTag tags, CancellationToken cancellationToken = default)
    {
        try
        {
            if (tags.HasFlag(ResponseCacheTag.Locations))
                await store.EvictByTagAsync(PublicCaching.LocationsTag, cancellationToken);
            if (tags.HasFlag(ResponseCacheTag.Photos))
                await store.EvictByTagAsync(PublicCaching.PhotosTag, cancellationToken);
            if (tags.HasFlag(ResponseCacheTag.Cities))
                await store.EvictByTagAsync(PublicCaching.CitiesTag, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not evict cached responses ({Tags}); they will expire on their own", tags);
        }
    }
}

/// <summary>
/// Test-only: turns the cache off for every request that does not send <see cref="PublicCaching.TestOptInHeader"/>.
/// The integration suite shares one server, so a cache that is always on would hand one test the response
/// another test cached, and the tests that DO exercise the cache opt in explicitly.
/// </summary>
internal sealed class TestOptInPolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        if (!context.HttpContext.Request.Headers.ContainsKey(PublicCaching.TestOptInHeader))
        {
            context.AllowCacheLookup = false;
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

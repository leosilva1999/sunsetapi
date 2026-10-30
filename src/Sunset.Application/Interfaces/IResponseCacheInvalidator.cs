namespace Sunset.Application.Interfaces;

/// <summary>The groups of cached public responses (see the output cache policies in the API).</summary>
[Flags]
public enum ResponseCacheTag
{
    None = 0,

    /// <summary>Location search/ranking/detail responses.</summary>
    Locations = 1,

    /// <summary>The photo feed and the photo lists of locations and cities.</summary>
    Photos = 2,

    /// <summary>The city lists and city pages (counts, covers and ranking depend on locations, photos and ratings).</summary>
    Cities = 4,

    All = Locations | Photos | Cities,
}

/// <summary>
/// Drops cached public responses when what they show changes, so a new location/photo/rating appears at once
/// instead of after the cache's time-to-live. Called AFTER the change is saved; failures must never break the
/// request that made the change (the time-to-live is the safety net).
/// </summary>
public interface IResponseCacheInvalidator
{
    Task InvalidateAsync(ResponseCacheTag tags, CancellationToken cancellationToken = default);
}

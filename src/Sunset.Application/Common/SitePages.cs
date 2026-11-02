using Sunset.Application.Interfaces;
using Sunset.Domain.Entities;

namespace Sunset.Application.Common;

/// <summary>Where things live on the public site (the site's routes, not the API's).</summary>
public static class SitePages
{
    public static string Location(string slug) => $"/locations/{slug}";

    public static string City(string slug) => $"/cidades/{slug}";

    /// <summary>
    /// The location's page and, when its city is loaded, the city's page. Best effort: a city that was not
    /// loaded is still covered by the "cities" tag that travels with every notification.
    /// </summary>
    public static SiteChange ForLocation(Location location, bool indexable)
    {
        var paths = new List<string>(2);
        if (!string.IsNullOrEmpty(location.Slug))
            paths.Add(Location(location.Slug));
        if (location.Municipality is { Slug.Length: > 0 } city)
            paths.Add(City(city.Slug));

        return new SiteChange(paths, indexable);
    }
}

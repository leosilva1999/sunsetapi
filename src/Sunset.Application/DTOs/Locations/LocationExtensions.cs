using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Locations;

public static class LocationExtensions
{
    /// <summary>City fields come from the Municipality/Locality navigations, which must have been loaded.</summary>
    public static LocationResponse ToResponse(this Location location, double? distanceKm = null, LocationStats? stats = null) =>
        new(location.Id, location.Name, location.Latitude, location.Longitude, location.City, location.AvgRating, location.CreatedAt,
            distanceKm is { } d ? Math.Round(d, 2) : null,
            location.Municipality?.Slug, location.Municipality?.Name, location.Municipality?.AdminArea,
            location.Locality?.Name, location.Locality?.Slug,
            stats?.RatingsCount ?? 0, stats?.PhotosCount ?? 0, stats?.CoverImageUrl,
            string.IsNullOrEmpty(location.Slug) ? null : location.Slug);

    /// <summary>Builds the responses of a batch of locations with ONE extra round trip for their counts and covers.</summary>
    public static async Task<List<LocationResponse>> ToResponsesAsync(
        this ILocationRepository repository,
        IReadOnlyCollection<(Location Location, double? DistanceKm)> items,
        CancellationToken cancellationToken = default)
    {
        var stats = await repository.GetStatsAsync(items.Select(i => i.Location.Id).ToList(), cancellationToken);

        return items
            .Select(i => i.Location.ToResponse(i.DistanceKm, stats.GetValueOrDefault(i.Location.Id)))
            .ToList();
    }

    public static async Task<LocationResponse> ToResponseAsync(
        this ILocationRepository repository, Location location, double? distanceKm = null, CancellationToken cancellationToken = default) =>
        (await repository.ToResponsesAsync([(location, distanceKm)], cancellationToken))[0];
}

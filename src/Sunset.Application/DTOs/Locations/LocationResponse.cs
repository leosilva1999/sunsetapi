namespace Sunset.Application.DTOs.Locations;

/// <param name="Slug">The public URL key, assigned at registration and never changed ("praia-do-rosa-imbituba-sc").</param>
/// <param name="City">The legacy free-text city. For a location tied to the catalog it is the canonical "{Name} - {State}".</param>
/// <param name="CitySlug">The catalog municipality (null while the location is not tied to one yet).</param>
/// <param name="LocalitySlug">The catalog locality inside it, when one was chosen.</param>
/// <param name="RatingsCount">Ratings of the location, counted when read.</param>
/// <param name="PhotosCount">Visible photos of the location, counted when read.</param>
/// <param name="CoverImageUrl">The most liked visible photo (ties: the most recent); null without photos.</param>
public sealed record LocationResponse(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    string City,
    decimal AvgRating,
    DateTime CreatedAt,
    double? DistanceKm = null,
    string? CitySlug = null,
    string? CityName = null,
    string? State = null,
    string? LocalityName = null,
    string? LocalitySlug = null,
    int RatingsCount = 0,
    int PhotosCount = 0,
    string? CoverImageUrl = null,
    string? Slug = null);

/// <summary>Counts and cover of one location, computed from its ratings and visible photos.</summary>
public sealed record LocationStats(int RatingsCount, int PhotosCount, string? CoverImageUrl)
{
    public static LocationStats Empty { get; } = new(0, 0, null);
}

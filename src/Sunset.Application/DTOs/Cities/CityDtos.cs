using System.Text.Json.Serialization;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Cities;

/// <summary>The municipality a locality belongs to.</summary>
public sealed record CityParentResponse(Guid Id, string Name, string State, string Slug);

/// <param name="LocationsCount">Visible locations tied to the city (for a municipality: its own and those of its localities).</param>
/// <param name="PhotosCount">Visible photos in those locations.</param>
/// <param name="RatingsCount">Ratings of those locations.</param>
/// <param name="AvgRating">Average of ALL those ratings (not the average of the locations' averages); null without ratings.</param>
/// <param name="CoverImageUrl">The most liked visible photo (ties: the most recent); null without photos.</param>
public sealed record CityResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CityKind Kind,
    string Name,
    string Country,
    string State,
    string Slug,
    CityParentResponse? Parent,
    string? TimeZoneId,
    int Population,
    double Latitude,
    double Longitude,
    int LocationsCount = 0,
    int PhotosCount = 0,
    int RatingsCount = 0,
    decimal? AvgRating = null,
    string? CoverImageUrl = null);

public sealed record CityTopLocationResponse(Guid Id, string Name, decimal AvgRating);

public sealed record CityLocalityResponse(string Name, string Slug, int LocationsCount);

/// <summary>Everything a city page needs in one call: the city, its counts, its best location and its localities.</summary>
public sealed record CityDetailResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CityKind Kind,
    string Name,
    string Country,
    string State,
    string Slug,
    string? TimeZoneId,
    int Population,
    double Latitude,
    double Longitude,
    int LocationsCount,
    int PhotosCount,
    int RatingsCount,
    decimal? AvgRating,
    string? CoverImageUrl,
    CityTopLocationResponse? TopLocation,
    IReadOnlyList<CityLocalityResponse> Localities);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CityKindFilter
{
    Municipality,
    Locality,
    Any
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CitySortOption
{
    /// <summary>Cities with the most locations first. Only cities that have locations can be ordered this way.</summary>
    Locations,

    /// <summary>Best rated first (average of all ratings). Only cities that have locations.</summary>
    Rating,

    /// <summary>Most populous first (the sensible order for an autocomplete).</summary>
    Population,

    /// <summary>Alphabetical.</summary>
    Name
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CityLocationSort
{
    Rating,
    Recent,
    Name
}

/// <param name="IncludeEmpty">False (default): only cities with at least one visible location. True: the whole
/// catalog (the registration autocomplete). Sorting by locations/rating always implies false.</param>
public sealed record CityListQuery(
    string? Q,
    CityKindFilter Kind,
    string? Country,
    string? State,
    CitySortOption Sort,
    string? Cursor,
    int Limit = 20,
    bool IncludeEmpty = false)
{
    public const int MinSearchLength = 2;

    public bool SortsByActivity => Sort is CitySortOption.Locations or CitySortOption.Rating;
}

public sealed record CityStats(int LocationsCount, int PhotosCount, int RatingsCount, decimal? AvgRating, string? CoverImageUrl)
{
    public static CityStats Empty { get; } = new(0, 0, 0, null, null);
}

public sealed record CityWithStats(City City, CityStats Stats);

public sealed record CityDetailExtras(CityTopLocationResponse? TopLocation, IReadOnlyList<CityLocalityResponse> Localities);

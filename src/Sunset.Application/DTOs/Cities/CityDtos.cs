using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Cities;

/// <summary>The municipality a locality belongs to.</summary>
public sealed record CityParentResponse(Guid Id, string Name, string State, string Slug);

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
    double Longitude);

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
    /// <summary>Most populous first (the sensible order for an autocomplete).</summary>
    Population,

    /// <summary>Alphabetical.</summary>
    Name
}

public sealed record CityListQuery(
    string? Q,
    CityKindFilter Kind,
    string? Country,
    string? State,
    CitySortOption Sort,
    string? Cursor,
    int Limit = 20)
{
    public const int MinSearchLength = 2;
}

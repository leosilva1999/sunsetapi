using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Cities;

/// <param name="Latitude">The pin; both coordinates are required.</param>
/// <param name="CityId">The city the person picked, to check the pin against (optional).</param>
public sealed record NearbyCitiesQuery(double? Latitude, double? Longitude, Guid? CityId, int Limit)
{
    public const int DefaultLimit = 3;
    public const int MaxLimit = 10;
}

/// <summary>A catalog place near a point. <paramref name="Parent"/> is the municipality of a locality.</summary>
public sealed record NearbyCityResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CityKind Kind,
    string Name,
    string State,
    string Slug,
    double DistanceKm,
    CityParentResponse? Parent);

/// <summary>How far the pin is from the city the person chose.</summary>
/// <param name="FarAway">True when the pin is more than <see cref="NearbyCitiesResponse.FarAwayKm"/> km from it: a warning, never a block.</param>
public sealed record ChosenCityCheckResponse(Guid Id, string Name, string State, double DistanceKm, bool FarAway);

/// <param name="Items">The nearest localities and municipalities to the pin, nearest first.</param>
/// <param name="ChosenCity">Only when a <c>cityId</c> was sent.</param>
public sealed record NearbyCitiesResponse(IReadOnlyList<NearbyCityResponse> Items, ChosenCityCheckResponse? ChosenCity)
{
    public const double FarAwayKm = 150;
}

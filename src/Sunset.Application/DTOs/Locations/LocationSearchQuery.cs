namespace Sunset.Application.DTOs.Locations;

public sealed record LocationSearchQuery(
    string? Q,
    double? Latitude,
    double? Longitude,
    double? RadiusKm,
    string? Cursor,
    int Limit = 20)
{
    public const double DefaultRadiusKm = 50;
    public const double MaxRadiusKm = 200;

    // "Near me" mode: results are filtered to the radius and ordered by distance instead of recency.
    public bool IsNearby => Latitude is not null && Longitude is not null;

    public double EffectiveRadiusKm => RadiusKm ?? DefaultRadiusKm;
}

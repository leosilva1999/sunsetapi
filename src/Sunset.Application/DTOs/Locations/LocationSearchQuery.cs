namespace Sunset.Application.DTOs.Locations;

public sealed record LocationSearchQuery(
    string? Q,
    double? Latitude,
    double? Longitude,
    double? RadiusKm,
    string? Cursor,
    int Limit = 20);

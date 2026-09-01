namespace Sunset.Application.DTOs.Locations;

public sealed record LocationResponse(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    string City,
    decimal AvgRating,
    DateTime CreatedAt);

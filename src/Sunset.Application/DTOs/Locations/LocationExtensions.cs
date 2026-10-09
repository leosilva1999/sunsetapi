using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Locations;

public static class LocationExtensions
{
    public static LocationResponse ToResponse(this Location location, double? distanceKm = null) =>
        new(location.Id, location.Name, location.Latitude, location.Longitude, location.City, location.AvgRating, location.CreatedAt,
            distanceKm is { } d ? Math.Round(d, 2) : null);
}

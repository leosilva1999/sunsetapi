using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Cities;

public static class CityExtensions
{
    public static CityResponse ToResponse(this City city, CityStats? stats = null) =>
        new(
            city.Id, city.Kind, city.Name, city.Country, city.AdminArea, city.Slug,
            city.ParentCity is { } parent ? new CityParentResponse(parent.Id, parent.Name, parent.AdminArea, parent.Slug) : null,
            city.TimeZoneId, city.Population, city.Latitude, city.Longitude,
            stats?.LocationsCount ?? 0, stats?.PhotosCount ?? 0, stats?.RatingsCount ?? 0, stats?.AvgRating, stats?.CoverImageUrl);

    public static CityResponse ToResponse(this CityWithStats item) => item.City.ToResponse(item.Stats);

    public static CityDetailResponse ToDetailResponse(this City city, CityStats stats, CityDetailExtras extras) =>
        new(
            city.Id, city.Kind, city.Name, city.Country, city.AdminArea, city.Slug, city.TimeZoneId, city.Population,
            city.Latitude, city.Longitude, stats.LocationsCount, stats.PhotosCount, stats.RatingsCount, stats.AvgRating,
            stats.CoverImageUrl, extras.TopLocation, extras.Localities);
}

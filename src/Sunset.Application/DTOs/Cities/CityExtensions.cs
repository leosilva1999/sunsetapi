using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Cities;

public static class CityExtensions
{
    public static CityResponse ToResponse(this City city) =>
        new(
            city.Id, city.Kind, city.Name, city.Country, city.AdminArea, city.Slug,
            city.ParentCity is { } parent ? new CityParentResponse(parent.Id, parent.Name, parent.AdminArea, parent.Slug) : null,
            city.TimeZoneId, city.Population, city.Latitude, city.Longitude);
}

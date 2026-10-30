namespace Sunset.Application.DTOs.Locations;

/// <param name="City">Legacy free-text city ("Imbituba - SC"). Optional when <paramref name="CityId"/> is sent.</param>
/// <param name="CityId">A catalog city (GET /cities): a municipality, or a locality inside one. Preferred.</param>
public sealed record CreateLocationRequest(string Name, double Latitude, double Longitude, string? City = null, Guid? CityId = null);

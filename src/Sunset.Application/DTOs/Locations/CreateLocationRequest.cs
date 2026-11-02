using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Locations;

/// <param name="City">Legacy free-text city ("Imbituba - SC"). Optional when <paramref name="CityId"/> is sent.</param>
/// <param name="CityId">A catalog city (GET /cities): a municipality, or a locality inside one. Preferred.</param>
/// <param name="Description">Optional, up to 1000 characters of plain text.</param>
/// <param name="Category">Optional kind of place; sent as its name ("Beach", "Viewpoint"...).</param>
public sealed record CreateLocationRequest(
    string Name,
    double Latitude,
    double Longitude,
    string? City = null,
    Guid? CityId = null,
    string? Description = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LocationCategory? Category = null);

using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Locations;

public sealed record LocationWithDistance(Location Location, double? DistanceKm);

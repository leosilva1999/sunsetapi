namespace Sunset.Application.DTOs.Locations;

public sealed record CreateLocationRequest(string Name, double Latitude, double Longitude, string City);

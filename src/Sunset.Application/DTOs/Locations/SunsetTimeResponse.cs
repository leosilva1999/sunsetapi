namespace Sunset.Application.DTOs.Locations;

public sealed record SunsetTimeResponse(
    DateOnly Date,
    string TzId,
    string UtcOffset,
    DateTimeOffset Sunrise,
    DateTimeOffset Sunset,
    DateTimeOffset SolarNoon,
    int DayLengthSeconds);

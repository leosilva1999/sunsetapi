using Sunset.Application.DTOs.Locations;

namespace Sunset.Application.Interfaces;

public interface ISunsetTimeService
{
    Task<SunsetTimeResponse> GetSunsetTimeAsync(double latitude, double longitude, DateOnly? date, CancellationToken cancellationToken = default);
}

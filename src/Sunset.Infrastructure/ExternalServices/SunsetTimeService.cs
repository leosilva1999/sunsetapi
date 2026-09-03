using System.Globalization;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.ExternalServices;

public class SunsetTimeService(HttpClient httpClient) : ISunsetTimeService
{
    public async Task<SunsetTimeResponse> GetSunsetTimeAsync(double latitude, double longitude, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lng = longitude.ToString(CultureInfo.InvariantCulture);
        var requestUri = $"v2?lat={lat}&lng={lng}";
        if (date is { } d)
            requestUri += $"&date={d:yyyy-MM-dd}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ExternalServiceException($"sunrise-sunset.org request failed with status {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SunsetTimeApiResponse>(cancellationToken)
            ?? throw new ExternalServiceException("sunrise-sunset.org returned an empty response.");

        return new SunsetTimeResponse(
            DateOnly.Parse(payload.Date, CultureInfo.InvariantCulture),
            payload.TzId,
            payload.UtcOffset,
            payload.Sunrise,
            payload.Sunset,
            payload.SolarNoon,
            payload.DayLength);
    }
}

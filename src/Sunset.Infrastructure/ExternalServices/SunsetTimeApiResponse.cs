using System.Text.Json.Serialization;

namespace Sunset.Infrastructure.ExternalServices;

internal sealed class SunsetTimeApiResponse
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = null!;

    [JsonPropertyName("tzid")]
    public string TzId { get; init; } = null!;

    [JsonPropertyName("utc_offset")]
    public string UtcOffset { get; init; } = null!;

    [JsonPropertyName("sunrise")]
    public DateTimeOffset Sunrise { get; init; }

    [JsonPropertyName("sunset")]
    public DateTimeOffset Sunset { get; init; }

    [JsonPropertyName("solar_noon")]
    public DateTimeOffset SolarNoon { get; init; }

    [JsonPropertyName("day_length")]
    public int DayLength { get; init; }
}

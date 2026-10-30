namespace Sunset.Application.DTOs.Moderation;

/// <param name="CityId">A catalog municipality, or a locality inside one.</param>
public sealed record ChangeLocationCityRequest(Guid CityId);

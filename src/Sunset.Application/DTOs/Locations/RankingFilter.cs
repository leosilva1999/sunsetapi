namespace Sunset.Application.DTOs.Locations;

/// <summary>Narrows the location ranking to a place. All set fields apply together (AND).</summary>
/// <param name="CityId">A municipality: locations tied to it, directly or through one of its localities.</param>
/// <param name="LocalityId">A locality: only the locations tied to it.</param>
/// <param name="State">Upper-case state code ("SC"): locations whose municipality is in it.</param>
public sealed record RankingFilter(Guid? CityId = null, Guid? LocalityId = null, string? State = null)
{
    public bool IsEmpty => CityId is null && LocalityId is null && State is null;
}

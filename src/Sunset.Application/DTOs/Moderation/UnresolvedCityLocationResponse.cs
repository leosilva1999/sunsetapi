using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;

namespace Sunset.Application.DTOs.Moderation;

/// <summary>A location still not tied to a catalog city, with what the system could work out about it.</summary>
/// <param name="CreatedByUserId">Who registered it (moderation only; null for old rows).</param>
/// <param name="Reason">"match" (a single municipality fits: confirm with one click), "ambiguous" (homonyms; see
/// <paramref name="Candidates"/>), "locality" (only a locality bears that name; confirming changes the displayed
/// city) or "none".</param>
/// <param name="SuggestedCity">The municipality to link (for "locality": the locality's municipality).</param>
public sealed record UnresolvedCityLocationResponse(
    LocationResponse Location,
    Guid? CreatedByUserId,
    string Reason,
    CityResponse? SuggestedCity,
    CityResponse? SuggestedLocality,
    IReadOnlyList<CityResponse> Candidates);

using System.Text.RegularExpressions;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Application.Cities;

public enum CityResolutionKind
{
    /// <summary>Exactly one municipality fits: safe to link on its own.</summary>
    Linked,

    /// <summary>Several municipalities share the name and the pin doesn't settle it: a person decides.</summary>
    Ambiguous,

    /// <summary>No municipality has that name, but a locality does: linking it would change the displayed city, so a person confirms.</summary>
    Locality,

    /// <summary>Nothing in the catalog matches.</summary>
    None
}

/// <param name="Municipality">The municipality (Linked), or the one to suggest (Locality: the locality's parent).</param>
/// <param name="Locality">Only for <see cref="CityResolutionKind.Locality"/>.</param>
/// <param name="Candidates">For Ambiguous: the homonyms, nearest to the pin first (at most 3).</param>
public sealed record CityResolution(CityResolutionKind Kind, City? Municipality, City? Locality, IReadOnlyList<City> Candidates)
{
    public static CityResolution Unmatched { get; } = new(CityResolutionKind.None, null, null, []);
}

/// <summary>
/// Turns the legacy free-text city ("Goiaminas - MG", "Santa Helena") into a catalog city, conservatively:
/// it links by itself only when the answer is unambiguous, and never guesses a locality.
/// </summary>
public sealed partial class CityResolver(ICityRepository cityRepository)
{
    // A homonym is settled by the pin only when the nearest candidate is close AND clearly nearer than the next one.
    public const double MaxSettleDistanceKm = 60;
    public const double SettleDistanceRatio = 2;

    [GeneratedRegex(@"^(.*?)\s*[-,/]\s*([A-Za-z]{2})$")]
    private static partial Regex NameAndStateRegex();

    public async Task<CityResolution> ResolveAsync(string? text, double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var (name, state) = Parse(text);
        var normalized = NameNormalizer.Normalize(name);
        if (normalized.Length < 2)
            return CityResolution.Unmatched;

        var municipalities = await cityRepository.FindMunicipalitiesAsync(normalized, state, cancellationToken);
        if (municipalities.Count == 1)
            return new CityResolution(CityResolutionKind.Linked, municipalities[0], null, []);

        if (municipalities.Count > 1)
        {
            var ranked = RankByDistance(municipalities, latitude, longitude);
            return IsSettled(ranked)
                ? new CityResolution(CityResolutionKind.Linked, ranked[0].City, null, [])
                : new CityResolution(CityResolutionKind.Ambiguous, null, null, ranked.Take(3).Select(r => r.City).ToList());
        }

        var localities = await cityRepository.FindLocalitiesAsync(normalized, state, cancellationToken);
        if (localities.Count == 0)
            return CityResolution.Unmatched;

        var nearest = RankByDistance(localities, latitude, longitude);
        return IsSettled(nearest)
            ? new CityResolution(CityResolutionKind.Locality, nearest[0].City.ParentCity, nearest[0].City, [])
            : CityResolution.Unmatched;
    }

    /// <summary>"Goiaminas - MG" -> ("Goiaminas", "MG"); "Santa Helena" -> ("Santa Helena", null).</summary>
    public static (string Name, string? State) Parse(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var match = NameAndStateRegex().Match(trimmed);

        return match.Success && match.Groups[1].Length > 0
            ? (match.Groups[1].Value.Trim(), match.Groups[2].Value.ToUpperInvariant())
            : (trimmed, null);
    }

    private static List<(City City, double DistanceKm)> RankByDistance(IReadOnlyList<City> cities, double latitude, double longitude) =>
        cities
            .Select(c => (City: c, DistanceKm: GeoMath.DistanceKm(latitude, longitude, c.Latitude, c.Longitude)))
            .OrderBy(x => x.DistanceKm)
            .ToList();

    private static bool IsSettled(List<(City City, double DistanceKm)> ranked)
    {
        if (ranked.Count == 0 || ranked[0].DistanceKm > MaxSettleDistanceKm)
            return false;

        return ranked.Count == 1 || ranked[1].DistanceKm >= SettleDistanceRatio * ranked[0].DistanceKm;
    }
}

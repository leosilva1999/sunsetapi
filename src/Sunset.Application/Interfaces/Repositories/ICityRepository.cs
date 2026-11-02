using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces.Repositories;

public interface ICityRepository
{
    /// <summary>Active cities only; the parent municipality is loaded for localities, and every item carries its counts.</summary>
    Task<CursorPagedResult<CityWithStats>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active municipalities ranked by the average of the ratings their locations received within the period
    /// (ties: more ratings first). Only municipalities with at least one rating in the period appear.
    /// </summary>
    Task<IReadOnlyList<CityRankingEntry>> GetRankingAsync(RankingPeriod period, int limit, CancellationToken cancellationToken = default);

    Task<City?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>An active city (with its parent loaded), or null.</summary>
    Task<City?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CityStats> GetStatsAsync(Guid cityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The best rated location of the place and — for a municipality only — its localities that have locations
    /// (by count, at most 10). A locality's best location is chosen among those tied to the locality itself.
    /// </summary>
    Task<CityDetailExtras> GetDetailExtrasAsync(Guid cityId, CityKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// The nearest active places (localities and municipalities) to a point, nearest first, by exact great-circle
    /// distance. Widens the search area step by step until <paramref name="limit"/> places are inside it; gives
    /// fewer (or none) when nothing is within the widest area.
    /// </summary>
    Task<IReadOnlyList<NearbyCityResponse>> GetNearestAsync(double latitude, double longitude, int limit, CancellationToken cancellationToken = default);

    /// <summary>Active municipalities whose name (or source name) equals the normalized name; optionally of one state.</summary>
    Task<IReadOnlyList<City>> FindMunicipalitiesAsync(string normalizedName, string? state, CancellationToken cancellationToken = default);

    /// <summary>Active localities whose name or any alternate name equals the normalized name, parent loaded.</summary>
    Task<IReadOnlyList<City>> FindLocalitiesAsync(string normalizedName, string? state, CancellationToken cancellationToken = default);
}

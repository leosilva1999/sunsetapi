using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface ICityRepository
{
    /// <summary>Active cities only; the parent municipality is loaded for localities, and every item carries its counts.</summary>
    Task<CursorPagedResult<CityWithStats>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default);

    Task<City?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>An active city (with its parent loaded), or null.</summary>
    Task<City?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CityStats> GetStatsAsync(Guid cityId, CancellationToken cancellationToken = default);

    /// <summary>The best rated location and the localities that have locations (by count, at most 10).</summary>
    Task<CityDetailExtras> GetDetailExtrasAsync(Guid municipalityId, CancellationToken cancellationToken = default);

    /// <summary>Active municipalities whose name (or source name) equals the normalized name; optionally of one state.</summary>
    Task<IReadOnlyList<City>> FindMunicipalitiesAsync(string normalizedName, string? state, CancellationToken cancellationToken = default);

    /// <summary>Active localities whose name or any alternate name equals the normalized name, parent loaded.</summary>
    Task<IReadOnlyList<City>> FindLocalitiesAsync(string normalizedName, string? state, CancellationToken cancellationToken = default);
}

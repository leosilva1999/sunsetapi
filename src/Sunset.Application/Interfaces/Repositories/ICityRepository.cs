using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface ICityRepository
{
    /// <summary>Active cities only; the parent municipality is loaded for localities.</summary>
    Task<CursorPagedResult<City>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default);

    Task<City?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

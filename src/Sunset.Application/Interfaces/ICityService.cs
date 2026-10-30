using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;

namespace Sunset.Application.Interfaces;

public interface ICityService
{
    Task<CursorPagedResult<CityResponse>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Public detail of a municipality. Localities have no public page yet (404).</summary>
    Task<CityResponse> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

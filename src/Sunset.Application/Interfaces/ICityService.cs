using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface ICityService
{
    Task<CursorPagedResult<CityResponse>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public page of a municipality in one call. An unknown slug, an inactive city, a locality (no public
    /// pages yet) and a municipality with no visible location are all 404.
    /// </summary>
    Task<CityDetailResponse> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<LocationResponse>> GetLocationsAsync(string slug, CityLocationSort sort, string? cursor, int limit, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(string slug, PhotoSortOption sort, string? cursor, int limit, Guid? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>Sunset time at the city's centre.</summary>
    Task<SunsetTimeResponse> GetSunsetTimeAsync(string slug, DateOnly? date, CancellationToken cancellationToken = default);
}

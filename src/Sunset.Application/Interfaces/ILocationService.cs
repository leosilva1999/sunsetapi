using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface ILocationService
{
    Task<CursorPagedResult<LocationResponse>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default);
    Task<LocationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LocationResponse> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocationResponse>> GetRankingAsync(RankingPeriod period, int limit, CancellationToken cancellationToken = default);
    Task<LocationResponse> RateAsync(Guid userId, Guid locationId, CreateRatingRequest request, CancellationToken cancellationToken = default);
    Task<SunsetTimeResponse> GetSunsetTimeAsync(Guid locationId, DateOnly? date, CancellationToken cancellationToken = default);
}

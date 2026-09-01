using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces.Repositories;

public interface ILocationRepository
{
    Task<Location?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Location>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default);
    Task AddAsync(Location location, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> GetRankingAsync(RankingPeriod period, int limit, CancellationToken cancellationToken = default);
    Task<Rating?> GetRatingAsync(Guid userId, Guid locationId, CancellationToken cancellationToken = default);
    Task AddRatingAsync(Rating rating, CancellationToken cancellationToken = default);
}

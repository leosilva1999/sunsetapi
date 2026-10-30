using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces.Repositories;

public interface ILocationRepository
{
    Task<Location?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Both bypass the soft-delete query filter: the moderation restore flow needs to see hidden rows.
    /// <summary>A visible location by its public slug (city navigations loaded).</summary>
    Task<Location?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>True when the slug is taken by ANY location, removed ones included (their slugs stay reserved).</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    Task<Location?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> GetDeletedAsync(int limit, CancellationToken cancellationToken = default);

    // Moves the source's ratings to the target, skipping users who already rated the target (the
    // (user, location) pair is unique) - those stay on the source. Returns how many moved.
    Task<int> MoveRatingsAsync(Guid sourceLocationId, Guid targetLocationId, CancellationToken cancellationToken = default);

    /// <summary>Ratings count, visible photos count and cover photo for each location, in one round trip. Missing ids = no data.</summary>
    Task<IReadOnlyDictionary<Guid, LocationStats>> GetStatsAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);

    /// <summary>The locations of one municipality (those tied to it directly or through a locality).</summary>
    Task<CursorPagedResult<Location>> GetByCityAsync(Guid cityId, CityLocationSort sort, string? cursor, int limit, CancellationToken cancellationToken = default);

    /// <summary>Visible locations not tied to a catalog city yet, newest first (the moderation review queue).</summary>
    Task<IReadOnlyList<Location>> GetWithoutCityAsync(int limit, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<LocationWithDistance>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default);
    Task AddAsync(Location location, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> GetRankingAsync(RankingPeriod period, int limit, CancellationToken cancellationToken = default);
    Task<Rating?> GetRatingAsync(Guid userId, Guid locationId, CancellationToken cancellationToken = default);
    Task AddRatingAsync(Rating rating, CancellationToken cancellationToken = default);
    Task RemoveRatingAsync(Rating rating, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<Rating>> GetRatingsAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<decimal> GetAverageRatingAsync(Guid locationId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

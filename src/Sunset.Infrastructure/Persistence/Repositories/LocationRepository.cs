using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class LocationRepository(SunsetDbContext context) : ILocationRepository
{
    public Task<Location?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<CursorPagedResult<Location>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default)
    {
        var locations = context.Locations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            locations = locations.Where(l => EF.Functions.Like(l.Name, $"%{term}%") || EF.Functions.Like(l.City, $"%{term}%"));
        }

        if (query.Latitude is { } lat && query.Longitude is { } lng && query.RadiusKm is { } radiusKm)
        {
            // Bounding-box approximation (1 degree of latitude ~= 111km); good enough for a coarse
            // radius filter without pushing trigonometric (Haversine) math down into SQL translation.
            var latDelta = radiusKm / 111.0;
            var lngDelta = radiusKm / (111.0 * Math.Max(Math.Cos(lat * Math.PI / 180.0), 0.01));

            locations = locations.Where(l =>
                l.Latitude >= lat - latDelta && l.Latitude <= lat + latDelta &&
                l.Longitude >= lng - lngDelta && l.Longitude <= lng + lngDelta);
        }

        var decoded = CreatedAtCursor.TryDecode(query.Cursor);
        if (decoded is { } c)
        {
            locations = locations.Where(l =>
                l.CreatedAt < c.CreatedAt ||
                (l.CreatedAt == c.CreatedAt && l.Id.CompareTo(c.Id) < 0));
        }

        var items = await locations
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > query.Limit;
        var page = items.Take(query.Limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<Location>(page, nextCursor, hasMore);
    }

    public async Task AddAsync(Location location, CancellationToken cancellationToken = default)
    {
        await context.Locations.AddAsync(location, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> GetRankingAsync(RankingPeriod period, int limit, CancellationToken cancellationToken = default)
    {
        var since = period switch
        {
            RankingPeriod.Week => DateTime.UtcNow.AddDays(-7),
            RankingPeriod.Month => DateTime.UtcNow.AddMonths(-1),
            _ => DateTime.MinValue
        };

        var ranked = await context.Ratings
            .Where(r => r.CreatedAt >= since)
            .GroupBy(r => r.LocationId)
            .Select(g => new { LocationId = g.Key, AvgScore = g.Average(r => r.Score), Count = g.Count() })
            .OrderByDescending(g => g.AvgScore)
            .ThenByDescending(g => g.Count)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var locationIds = ranked.Select(r => r.LocationId).ToList();
        var locationsById = await context.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        return locationIds
            .Where(locationsById.ContainsKey)
            .Select(id => locationsById[id])
            .ToList();
    }

    public Task<Rating?> GetRatingAsync(Guid userId, Guid locationId, CancellationToken cancellationToken = default) =>
        context.Ratings.FirstOrDefaultAsync(r => r.UserId == userId && r.LocationId == locationId, cancellationToken);

    public async Task AddRatingAsync(Rating rating, CancellationToken cancellationToken = default)
    {
        await context.Ratings.AddAsync(rating, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

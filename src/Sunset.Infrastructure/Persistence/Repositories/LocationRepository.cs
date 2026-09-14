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

            // MySQL's default innodb_ft_min_token_size is 3, so FULLTEXT can't match anything
            // shorter - fall back to LIKE for short terms. That's also exactly where a LIKE
            // '%term%' scan is cheap (it only gets expensive once matches get more selective,
            // which is the 3+ character case the FULLTEXT index exists for).
            var booleanQuery = BuildBooleanModeQuery(term);
            locations = booleanQuery is null
                ? locations.Where(l => EF.Functions.Like(l.Name, $"%{term}%") || EF.Functions.Like(l.City, $"%{term}%"))
                : locations.Where(l => EF.Functions.Match(new[] { l.Name, l.City }, booleanQuery, MySqlMatchSearchMode.Boolean) > 0);
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

    // Builds a MySQL boolean-mode fulltext search string (each word required, prefix-matched:
    // "+word*"), or null when the term can't productively use the FULLTEXT index (too short, or
    // made up entirely of words below MySQL's minimum indexed token length) - callers should fall
    // back to a LIKE scan in that case. Strips boolean-mode operator characters (+-<>()~*"@) from
    // each word since we're the ones adding the operators; letting user input through unescaped
    // would let a search term change the query's meaning (e.g. a leading "-" excludes a term).
    private static string? BuildBooleanModeQuery(string term)
    {
        if (term.Length < 3)
            return null;

        var words = term
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length >= 3)
            .Select(word => $"+{word}*")
            .ToList();

        return words.Count == 0 ? null : string.Join(' ', words);
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
        context.Ratings.Include(r => r.User).FirstOrDefaultAsync(r => r.UserId == userId && r.LocationId == locationId, cancellationToken);

    public async Task AddRatingAsync(Rating rating, CancellationToken cancellationToken = default)
    {
        await context.Ratings.AddAsync(rating, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveRatingAsync(Rating rating, CancellationToken cancellationToken = default)
    {
        context.Ratings.Remove(rating);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPagedResult<Rating>> GetRatingsAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var ratings = context.Ratings
            .Include(r => r.User)
            .Where(r => r.LocationId == locationId)
            .AsQueryable();

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            ratings = ratings.Where(x =>
                x.CreatedAt < c.CreatedAt ||
                (x.CreatedAt == c.CreatedAt && x.Id.CompareTo(c.Id) < 0));
        }

        var items = await ratings
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<Rating>(page, nextCursor, hasMore);
    }

    public async Task<decimal> GetAverageRatingAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        var ratings = context.Ratings.Where(r => r.LocationId == locationId);
        if (!await ratings.AnyAsync(cancellationToken))
            return 0m;

        var average = await ratings.AverageAsync(r => r.Score, cancellationToken);
        return Math.Round((decimal)average, 2);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

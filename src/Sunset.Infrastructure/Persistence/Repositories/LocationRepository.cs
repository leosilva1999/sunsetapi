using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence.Configurations;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class LocationRepository(SunsetDbContext context) : ILocationRepository
{
    public Task<Location?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<CursorPagedResult<LocationWithDistance>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default)
    {
        var locations = context.Locations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();

            // Terms under 3 characters aren't worth a full-text lookup (and would match nearly
            // everything as a prefix) - fall back to a substring ILIKE, which is cheap there.
            var tsQuery = BuildPrefixTsQuery(term);
            locations = tsQuery is null
                ? locations.Where(l => EF.Functions.ILike(l.Name, $"%{term}%") || EF.Functions.ILike(l.City, $"%{term}%"))
                : locations.Where(l => EF.Property<NpgsqlTsVector>(l, LocationConfiguration.LocationSearchVector).Matches(EF.Functions.ToTsQuery("simple", tsQuery)));
        }

        if (query.IsNearby)
            return await SearchNearbyAsync(locations, query, cancellationToken);

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

        return new CursorPagedResult<LocationWithDistance>(
            page.Select(l => new LocationWithDistance(l, null)).ToList(), nextCursor, hasMore);
    }

    // "Near me": exact great-circle (Haversine) distance computed in SQL, filtered to the radius and
    // ordered nearest-first. A bounding box runs first so the (Latitude, Longitude) index can discard
    // most rows before the trigonometry is evaluated; the Haversine check then trims the box's corners.
    private static async Task<CursorPagedResult<LocationWithDistance>> SearchNearbyAsync(
        IQueryable<Location> locations, LocationSearchQuery query, CancellationToken cancellationToken)
    {
        const double EarthRadiusKm = 6371.0;
        const double DegToRad = Math.PI / 180.0;

        var lat = query.Latitude!.Value;
        var lng = query.Longitude!.Value;
        var radiusKm = query.EffectiveRadiusKm;

        var latDelta = radiusKm / 111.0;
        var lngDelta = radiusKm / (111.0 * Math.Max(Math.Cos(lat * DegToRad), 0.01));
        var latRad = lat * DegToRad;
        var cosLat = Math.Cos(latRad);

        locations = locations.Where(l =>
            l.Latitude >= lat - latDelta && l.Latitude <= lat + latDelta &&
            l.Longitude >= lng - lngDelta && l.Longitude <= lng + lngDelta);

        // Math.Min(1, ...) guards ASIN against floating-point overshoot (ASIN(1.0000000002) is NULL in MySQL).
        var withDistance = locations.Select(l => new
        {
            Location = l,
            DistanceKm = 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(
                Math.Sin((l.Latitude * DegToRad - latRad) / 2) * Math.Sin((l.Latitude * DegToRad - latRad) / 2) +
                cosLat * Math.Cos(l.Latitude * DegToRad) *
                Math.Sin((l.Longitude - lng) * DegToRad / 2) * Math.Sin((l.Longitude - lng) * DegToRad / 2))))
        }).Where(x => x.DistanceKm <= radiusKm);

        var decoded = DistanceCursor.TryDecode(query.Cursor);
        if (decoded is { } c)
        {
            withDistance = withDistance.Where(x =>
                x.DistanceKm > c.DistanceKm ||
                (x.DistanceKm == c.DistanceKm && x.Location.Id.CompareTo(c.Id) > 0));
        }

        var items = await withDistance
            .OrderBy(x => x.DistanceKm)
            .ThenBy(x => x.Location.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > query.Limit;
        var page = items.Take(query.Limit).ToList();
        var nextCursor = hasMore ? DistanceCursor.Encode(page[^1].DistanceKm, page[^1].Location.Id) : null;

        return new CursorPagedResult<LocationWithDistance>(
            page.Select(x => new LocationWithDistance(x.Location, x.DistanceKm)).ToList(), nextCursor, hasMore);
    }

    // Builds a to_tsquery string (each word required, prefix-matched: "word:*" joined by "&"), or
    // null when the term is too short to be worth a full-text lookup - callers should fall back to
    // an ILIKE scan in that case. Keeps only letters/digits of each word since we're the ones adding
    // the operators; letting user input through unescaped would let a search term change the
    // query's meaning (and make to_tsquery throw a syntax error).
    private static string? BuildPrefixTsQuery(string term)
    {
        if (term.Length < 3)
            return null;

        var words = term
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length >= 3)
            .Select(word => $"{word}:*")
            .ToList();

        return words.Count == 0 ? null : string.Join(" & ", words);
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
            _ => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
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

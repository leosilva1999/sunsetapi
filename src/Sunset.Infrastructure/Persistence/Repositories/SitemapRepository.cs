using System.Text;
using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Sitemap;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Enums;

namespace Sunset.Infrastructure.Persistence.Repositories;

/// <summary>
/// Minimal projections for the sitemap: id, slug and a "last modified" worked out in the query (no column of its
/// own). Paged by id, which stays put when rows are added; the latest-change dates are then fetched for the
/// page's ids only, in grouped queries, not per row.
/// </summary>
public class SitemapRepository(SunsetDbContext context) : ISitemapRepository
{
    public async Task<CursorPagedResult<SitemapEntryResponse>> GetCitiesAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var cities = context.Cities.AsNoTracking()
            .Where(c => c.IsActive && c.Kind == CityKind.Municipality && context.Locations.Any(l => l.CityId == c.Id));
        if (GuidCursor.TryDecode(cursor) is { } after)
            cities = cities.Where(c => c.Id.CompareTo(after) > 0);

        var rows = await cities.OrderBy(c => c.Id).Take(limit + 1).Select(c => new { c.Id, c.Slug }).ToListAsync(cancellationToken);
        var (page, hasMore) = Trim(rows, limit);
        var ids = page.Select(r => r.Id).ToList();

        var locationDates = await context.Locations
            .Where(l => l.CityId != null && ids.Contains(l.CityId.Value))
            .GroupBy(l => l.CityId!.Value)
            .Select(g => new { Key = g.Key, At = g.Max(l => l.CreatedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.At, cancellationToken);

        var photoDates = await (from l in context.Locations
                                where l.CityId != null && ids.Contains(l.CityId.Value)
                                join p in context.Photos on l.Id equals p.LocationId
                                group p by l.CityId!.Value into g
                                select new { Key = g.Key, At = g.Max(p => p.CreatedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.At, cancellationToken);

        var ratingDates = await (from l in context.Locations
                                 where l.CityId != null && ids.Contains(l.CityId.Value)
                                 join r in context.Ratings on l.Id equals r.LocationId
                                 group r by l.CityId!.Value into g
                                 select new { Key = g.Key, At = g.Max(r => r.CreatedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.At, cancellationToken);

        return Build(page.Select(r => new SitemapEntryResponse(r.Id, r.Slug, Latest(locationDates, photoDates, ratingDates, r.Id))).ToList(), hasMore);
    }

    public async Task<CursorPagedResult<SitemapEntryResponse>> GetLocationsAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var locations = context.Locations.AsNoTracking().AsQueryable();
        if (GuidCursor.TryDecode(cursor) is { } after)
            locations = locations.Where(l => l.Id.CompareTo(after) > 0);

        var rows = await locations.OrderBy(l => l.Id).Take(limit + 1).Select(l => new { l.Id, l.Slug, l.CreatedAt }).ToListAsync(cancellationToken);
        var (page, hasMore) = Trim(rows, limit);
        var ids = page.Select(r => r.Id).ToList();

        var photoDates = await context.Photos
            .Where(p => ids.Contains(p.LocationId))
            .GroupBy(p => p.LocationId)
            .Select(g => new { Key = g.Key, At = g.Max(p => p.CreatedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.At, cancellationToken);

        var ratingDates = await context.Ratings
            .Where(r => ids.Contains(r.LocationId))
            .GroupBy(r => r.LocationId)
            .Select(g => new { Key = g.Key, At = g.Max(r => r.CreatedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.At, cancellationToken);

        return Build(page.Select(r => new SitemapEntryResponse(
            r.Id, r.Slug, Latest(new Dictionary<Guid, DateTime> { [r.Id] = r.CreatedAt }, photoDates, ratingDates, r.Id))).ToList(), hasMore);
    }

    public async Task<CursorPagedResult<SitemapEntryResponse>> GetPhotosAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var photos = context.Photos.AsNoTracking().AsQueryable();
        if (GuidCursor.TryDecode(cursor) is { } after)
            photos = photos.Where(p => p.Id.CompareTo(after) > 0);

        var rows = await photos.OrderBy(p => p.Id).Take(limit + 1).Select(p => new { p.Id, p.CreatedAt }).ToListAsync(cancellationToken);
        var (page, hasMore) = Trim(rows, limit);

        return Build(page.Select(r => new SitemapEntryResponse(r.Id, null, r.CreatedAt)).ToList(), hasMore);
    }

    private static (List<T> Page, bool HasMore) Trim<T>(List<T> rows, int limit) =>
        (rows.Take(limit).ToList(), rows.Count > limit);

    private static CursorPagedResult<SitemapEntryResponse> Build(List<SitemapEntryResponse> page, bool hasMore) =>
        new(page, hasMore && page.Count > 0 ? GuidCursor.Encode(page[^1].Id) : null, hasMore);

    private static DateTime Latest(Dictionary<Guid, DateTime> a, Dictionary<Guid, DateTime> b, Dictionary<Guid, DateTime> c, Guid id)
    {
        var latest = DateTime.MinValue;
        foreach (var source in new[] { a, b, c })
            if (source.TryGetValue(id, out var at) && at > latest)
                latest = at;

        return DateTime.SpecifyKind(latest, DateTimeKind.Utc);
    }
}

/// <summary>Cursor of an order by id: just the last id returned.</summary>
internal static class GuidCursor
{
    public static string Encode(Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString()));

    public static Guid? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            return Guid.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

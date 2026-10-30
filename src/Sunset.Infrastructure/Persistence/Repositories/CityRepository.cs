using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence.Configurations;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class CityRepository(SunsetDbContext context) : ICityRepository
{
    // (city, location) pairs. A location counts for its municipality (Location.CityId, always set) AND,
    // when it has one, for its locality (Location.LocalityId) - so one query shape serves both kinds.
    // A class built with member initializers (not a record built through its constructor): EF can translate a
    // UNION of two such projections, but not one over a constructor call.
    private sealed class CityLocationKey
    {
        public Guid Key { get; init; }
        public Guid LocationId { get; init; }
    }

    private sealed record Activity(int LocationsCount, int RatingsCount, double? RatingAverage);

    public async Task<CursorPagedResult<CityWithStats>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default)
    {
        var cities = BuildFilter(query);

        return query.SortsByActivity
            ? await SearchByActivityAsync(cities, query, cancellationToken)
            : await SearchByColumnAsync(cities, query, cancellationToken);
    }

    private IQueryable<City> BuildFilter(CityListQuery query)
    {
        var cities = context.Cities.AsNoTracking().Where(c => c.IsActive);

        cities = query.Kind switch
        {
            CityKindFilter.Municipality => cities.Where(c => c.Kind == CityKind.Municipality),
            CityKindFilter.Locality => cities.Where(c => c.Kind == CityKind.Locality),
            _ => cities,
        };

        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            var country = query.Country.Trim().ToUpperInvariant();
            cities = cities.Where(c => c.Country == country);
        }

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            var state = query.State.Trim().ToUpperInvariant();
            cities = cities.Where(c => c.AdminArea == state);
        }

        var tsQuery = BuildPrefixTsQuery(query.Q);
        if (tsQuery is not null)
            cities = cities.Where(c => EF.Property<NpgsqlTsVector>(c, CityConfiguration.CitySearchVector).Matches(EF.Functions.ToTsQuery("simple", tsQuery)));

        return cities;
    }

    // ---- ordered by a column (population / name): paged in SQL
    private async Task<CursorPagedResult<CityWithStats>> SearchByColumnAsync(IQueryable<City> cities, CityListQuery query, CancellationToken cancellationToken)
    {
        if (!query.IncludeEmpty)
            cities = cities.Where(c => context.Locations.Any(l => l.CityId == c.Id || l.LocalityId == c.Id));

        List<City> items;
        if (query.Sort == CitySortOption.Name)
        {
            if (CityNameCursor.TryDecode(query.Cursor) is { } c)
                cities = cities.Where(x => x.Name.CompareTo(c.Name) > 0 || (x.Name == c.Name && x.Id.CompareTo(c.Id) > 0));

            items = await cities.Include(x => x.ParentCity)
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Take(query.Limit + 1)
                .ToListAsync(cancellationToken);
        }
        else
        {
            if (CityPopulationCursor.TryDecode(query.Cursor) is { } c)
                cities = cities.Where(x => x.Population < c.Population || (x.Population == c.Population && x.Id.CompareTo(c.Id) > 0));

            items = await cities.Include(x => x.ParentCity)
                .OrderByDescending(x => x.Population).ThenBy(x => x.Id)
                .Take(query.Limit + 1)
                .ToListAsync(cancellationToken);
        }

        var hasMore = items.Count > query.Limit;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        var nextCursor = hasMore && items.Count > 0
            ? query.Sort == CitySortOption.Name
                ? CityNameCursor.Encode(items[^1].Name, items[^1].Id)
                : CityPopulationCursor.Encode(items[^1].Population, items[^1].Id)
            : null;

        var stats = await GetStatsByCityAsync(items.Select(i => i.Id).ToList(), cancellationToken);
        return new CursorPagedResult<CityWithStats>(
            items.Select(i => new CityWithStats(i, stats.GetValueOrDefault(i.Id, CityStats.Empty))).ToList(), nextCursor, hasMore);
    }

    // ---- ordered by activity (locations / rating): only cities that HAVE locations can be ranked, so the
    // candidate set is bounded by the number of locations; it is ordered and paged in memory.
    private async Task<CursorPagedResult<CityWithStats>> SearchByActivityAsync(IQueryable<City> cities, CityListQuery query, CancellationToken cancellationToken)
    {
        var activity = await GetActivityAsync(cancellationToken);
        var ids = activity.Keys.ToList();

        var candidates = await cities.Where(c => ids.Contains(c.Id)).Include(c => c.ParentCity).ToListAsync(cancellationToken);

        decimal SortKey(City c) => query.Sort == CitySortOption.Locations
            ? activity[c.Id].LocationsCount
            : AverageOf(activity[c.Id]) ?? -1m;

        var ordered = candidates.OrderByDescending(SortKey).ThenBy(c => c.Id).ToList();

        if (DecimalCursor.TryDecode(query.Cursor) is { } cursor)
            ordered = ordered.Where(c => SortKey(c) < cursor.Value || (SortKey(c) == cursor.Value && c.Id.CompareTo(cursor.Id) > 0)).ToList();

        var hasMore = ordered.Count > query.Limit;
        var page = ordered.Take(query.Limit).ToList();
        var nextCursor = hasMore ? DecimalCursor.Encode(SortKey(page[^1]), page[^1].Id) : null;

        var stats = await GetStatsByCityAsync(page.Select(c => c.Id).ToList(), cancellationToken);
        return new CursorPagedResult<CityWithStats>(
            page.Select(c => new CityWithStats(c, stats.GetValueOrDefault(c.Id, CityStats.Empty))).ToList(), nextCursor, hasMore);
    }

    public Task<City?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        context.Cities.AsNoTracking()
            .Include(c => c.ParentCity)
            .FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive, cancellationToken);

    // Tracked on purpose: the result is assigned to a Location's navigation, and EF must not see a second
    // instance of a City it already tracks.
    public Task<City?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Cities
            .Include(c => c.ParentCity)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive, cancellationToken);

    public async Task<CityStats> GetStatsAsync(Guid cityId, CancellationToken cancellationToken = default) =>
        (await GetStatsByCityAsync([cityId], cancellationToken)).GetValueOrDefault(cityId, CityStats.Empty);

    public async Task<CityDetailExtras> GetDetailExtrasAsync(Guid municipalityId, CancellationToken cancellationToken = default)
    {
        var top = await context.Locations
            .Where(l => l.CityId == municipalityId)
            .OrderByDescending(l => l.AvgRating)
            .ThenByDescending(l => l.Ratings.Count)
            .ThenByDescending(l => l.CreatedAt)
            .Select(l => new CityTopLocationResponse(l.Id, l.Name, l.AvgRating))
            .FirstOrDefaultAsync(cancellationToken);

        var counts = await context.Locations
            .Where(l => l.CityId == municipalityId && l.LocalityId != null)
            .GroupBy(l => l.LocalityId!.Value)
            .Select(g => new { LocalityId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(cancellationToken);

        var localityIds = counts.Select(x => x.LocalityId).ToList();
        var localities = await context.Cities.AsNoTracking()
            .Where(c => localityIds.Contains(c.Id) && c.IsActive)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return new CityDetailExtras(
            top,
            counts.Where(x => localities.ContainsKey(x.LocalityId))
                .Select(x => new CityLocalityResponse(localities[x.LocalityId].Name, localities[x.LocalityId].Slug, x.Count))
                .ToList());
    }

    // Both Find* are TRACKED on purpose: what they return is assigned to a new Location's navigation, and
    // adding that Location would otherwise mark an untracked (detached) City in its graph as Added and try
    // to INSERT a city that already exists.
    public async Task<IReadOnlyList<City>> FindMunicipalitiesAsync(string normalizedName, string? state, CancellationToken cancellationToken = default)
    {
        var candidates = await FilterByName(context.Cities.Where(c => c.IsActive && c.Kind == CityKind.Municipality), normalizedName, state)
            .OrderByDescending(c => c.Population)
            .Take(200)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(c => NameNormalizer.Normalize(c.Name) == normalizedName || NameNormalizer.Normalize(c.SourceName) == normalizedName)
            .ToList();
    }

    public async Task<IReadOnlyList<City>> FindLocalitiesAsync(string normalizedName, string? state, CancellationToken cancellationToken = default)
    {
        var candidates = await FilterByName(context.Cities.Where(c => c.IsActive && c.Kind == CityKind.Locality), normalizedName, state)
            .Include(c => c.ParentCity)
            .OrderByDescending(c => c.Population)
            .Take(200)
            .ToListAsync(cancellationToken);

        // SearchText is the normalized name, source name and alternate names joined by spaces: look for the whole phrase.
        var phrase = $" {normalizedName} ";
        return candidates.Where(c => $" {c.SearchText} ".Contains(phrase) && c.ParentCity is not null).ToList();
    }

    private static IQueryable<City> FilterByName(IQueryable<City> cities, string normalizedName, string? state)
    {
        if (!string.IsNullOrWhiteSpace(state))
        {
            var upper = state.Trim().ToUpperInvariant();
            cities = cities.Where(c => c.AdminArea == upper);
        }

        var tsQuery = BuildPrefixTsQuery(normalizedName);
        return tsQuery is null
            ? cities
            : cities.Where(c => EF.Property<NpgsqlTsVector>(c, CityConfiguration.CitySearchVector).Matches(EF.Functions.ToTsQuery("simple", tsQuery)));
    }

    // ---- counts

    private IQueryable<CityLocationKey> Keys(IReadOnlyCollection<Guid>? cityIds)
    {
        if (cityIds is null)
        {
            return context.Locations.Where(l => l.CityId != null).Select(l => new CityLocationKey { Key = l.CityId!.Value, LocationId = l.Id })
                .Concat(context.Locations.Where(l => l.LocalityId != null).Select(l => new CityLocationKey { Key = l.LocalityId!.Value, LocationId = l.Id }));
        }

        return context.Locations.Where(l => l.CityId != null && cityIds.Contains(l.CityId.Value)).Select(l => new CityLocationKey { Key = l.CityId!.Value, LocationId = l.Id })
            .Concat(context.Locations.Where(l => l.LocalityId != null && cityIds.Contains(l.LocalityId.Value)).Select(l => new CityLocationKey { Key = l.LocalityId!.Value, LocationId = l.Id }));
    }

    // Locations and ratings of every city that has at least one location - what the activity sorts need.
    private async Task<Dictionary<Guid, Activity>> GetActivityAsync(CancellationToken cancellationToken)
    {
        var keys = Keys(null);

        var locations = await keys.GroupBy(k => k.Key)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var ratings = await (from k in keys
                             join r in context.Ratings on k.LocationId equals r.LocationId
                             group r by k.Key into g
                             select new { Key = g.Key, Count = g.Count(), Average = g.Average(r => (double)r.Score) })
            .ToDictionaryAsync(x => x.Key, cancellationToken);

        return locations.ToDictionary(
            x => x.Key,
            x => ratings.TryGetValue(x.Key, out var r) ? new Activity(x.Count, r.Count, r.Average) : new Activity(x.Count, 0, null));
    }

    // Full counts and cover for a handful of cities (a page): a few grouped queries, not one per city.
    private async Task<Dictionary<Guid, CityStats>> GetStatsByCityAsync(IReadOnlyCollection<Guid> cityIds, CancellationToken cancellationToken)
    {
        if (cityIds.Count == 0)
            return [];

        var keys = Keys(cityIds);

        var locations = await keys.GroupBy(k => k.Key)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var photos = await (from k in keys
                            join p in context.Photos on k.LocationId equals p.LocationId
                            group p by k.Key into g
                            select new
                            {
                                Key = g.Key,
                                Count = g.Count(),
                                Cover = g.OrderByDescending(p => p.LikesCount).ThenByDescending(p => p.CreatedAt).Select(p => p.ImageUrl).FirstOrDefault(),
                            })
            .ToDictionaryAsync(x => x.Key, cancellationToken);

        var ratings = await (from k in keys
                             join r in context.Ratings on k.LocationId equals r.LocationId
                             group r by k.Key into g
                             select new { Key = g.Key, Count = g.Count(), Average = g.Average(r => (double)r.Score) })
            .ToDictionaryAsync(x => x.Key, cancellationToken);

        return locations.ToDictionary(
            x => x.Key,
            x => new CityStats(
                x.Value,
                photos.TryGetValue(x.Key, out var p) ? p.Count : 0,
                ratings.TryGetValue(x.Key, out var r) ? r.Count : 0,
                ratings.TryGetValue(x.Key, out r) ? Math.Round((decimal)r.Average, 2) : null,
                photos.TryGetValue(x.Key, out p) ? p.Cover : null));
    }

    private static decimal? AverageOf(Activity activity) =>
        activity.RatingAverage is { } average ? Math.Round((decimal)average, 2) : null;

    // Every word becomes a prefix term ("sao jo" -> "sao:* & jo:*"); the normalizer leaves only letters
    // and digits, so nothing can inject tsquery syntax.
    private static string? BuildPrefixTsQuery(string? term)
    {
        var words = NameNormalizer.Normalize(term)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => $"{word}:*")
            .ToList();

        return words.Count == 0 ? null : string.Join(" & ", words);
    }
}

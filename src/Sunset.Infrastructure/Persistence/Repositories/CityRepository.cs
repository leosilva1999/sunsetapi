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
    public async Task<CursorPagedResult<City>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default)
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

        return new CursorPagedResult<City>(items, nextCursor, hasMore);
    }

    public Task<City?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        context.Cities.AsNoTracking()
            .Include(c => c.ParentCity)
            .FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive, cancellationToken);

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

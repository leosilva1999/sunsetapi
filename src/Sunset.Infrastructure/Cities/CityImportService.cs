using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sunset.Application.Cities.Import;
using Sunset.Application.Common;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence;

namespace Sunset.Infrastructure.Cities;

public sealed record CityImportResult(int Inserted, int Updated, int Unchanged, int Reactivated, int Deactivated);

/// <summary>
/// Writes a <see cref="CityImportPlan"/> to the database. Idempotent: records are matched by
/// (source, external id) and updated in place, so running it again changes nothing; a stored slug is
/// never regenerated; a record that vanished from the dataset is deactivated, not deleted.
/// </summary>
public sealed class CityImportService(SunsetDbContext db, ILogger<CityImportService> logger)
{
    private const int BatchSize = 2000;

    public async Task<CityImportResult> ImportAsync(CityImportPlan plan, string country, CancellationToken cancellationToken = default)
    {
        country = country.ToUpperInvariant();

        // Thousands of tracked rows: detect changes once per batch (below), not on every access.
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var existing = await db.Cities.Where(c => c.Country == country).ToListAsync(cancellationToken);
        var byExternal = existing.Where(c => c.ExternalId is not null).ToDictionary(c => (c.Source, c.ExternalId!.Value));
        var manualByCode = existing
            .Where(c => c.Source == CityImportPlanner.ManualSource && c.NationalCode is not null)
            .GroupBy(c => c.NationalCode!)
            .ToDictionary(g => g.Key, g => g.First());

        // Slugs are unique across ALL countries.
        var slugs = new HashSet<string>(await db.Cities.Select(c => c.Slug).ToListAsync(cancellationToken));

        var seen = new HashSet<Guid>();
        var municipalityByKey = new Dictionary<string, City>();
        int inserted = 0, updated = 0, unchanged = 0, reactivated = 0, pending = 0;

        async Task FlushAsync(bool force = false)
        {
            if (pending == 0 || (!force && pending < BatchSize))
                return;

            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(cancellationToken);
            pending = 0;
        }

        City Upsert(PlannedCity p, City? parent)
        {
            var parentId = parent?.Id;
            var entity = Find(p);

            if (entity is null)
            {
                var baseSlug = p.Kind == CityKind.Municipality
                    ? SlugGenerator.ForMunicipality(p.Name, p.AdminArea)
                    : SlugGenerator.ForLocality(p.Name, parent!.Slug);
                var slug = SlugGenerator.Unique(baseSlug, slugs.Contains);
                slugs.Add(slug);

                entity = new City(p.Kind, p.Name, p.SourceName, p.AlternateNames, p.SearchText, p.Country, p.AdminArea, slug,
                    p.Source, p.ExternalId, p.NationalCode, parentId, p.Latitude, p.Longitude, p.TimeZoneId, p.Population);
                db.Cities.Add(entity);
                inserted++;
                pending++;
            }
            else
            {
                var wasInactive = !entity.IsActive;
                if (wasInactive)
                    entity.Activate();

                if (HasChanged(entity, p, parentId))
                {
                    entity.UpdateFromSource(p.Name, p.SourceName, p.AlternateNames, p.SearchText, p.AdminArea, parentId,
                        p.Latitude, p.Longitude, p.TimeZoneId, p.Population);
                    updated++;
                    pending++;
                }
                else if (wasInactive)
                {
                    reactivated++;
                    pending++;
                }
                else
                {
                    unchanged++;
                }
            }

            seen.Add(entity.Id);
            return entity;
        }

        City? Find(PlannedCity p)
        {
            if (p.ExternalId is { } externalId)
            {
                if (byExternal.TryGetValue((p.Source, externalId), out var found))
                    return found;

                // The dataset now carries a municipality we had added by hand: keep that row (and its slug).
                if (p.Kind == CityKind.Municipality && p.NationalCode is not null && manualByCode.Remove(p.NationalCode, out var manual))
                {
                    manual.AdoptSource(p.Source, externalId);
                    byExternal[(p.Source, externalId)] = manual;
                    return manual;
                }

                return null;
            }

            return p.NationalCode is not null && manualByCode.TryGetValue(p.NationalCode, out var m) ? m : null;
        }

        foreach (var p in plan.Municipalities)
        {
            municipalityByKey[p.Key] = Upsert(p, parent: null);
            await FlushAsync();
        }

        // Parents must be stored before their localities.
        await FlushAsync(force: true);

        foreach (var p in plan.Localities)
        {
            Upsert(p, municipalityByKey[p.ParentKey!]);
            await FlushAsync();
        }

        // Whatever of this country was not seen this time has left the dataset.
        var deactivated = 0;
        foreach (var city in existing.Where(c => c.IsActive && !seen.Contains(c.Id)))
        {
            city.Deactivate();
            deactivated++;
            pending++;
        }

        await FlushAsync(force: true);

        logger.LogInformation(
            "City import for {Country}: {Inserted} inserted, {Updated} updated, {Unchanged} unchanged, {Reactivated} reactivated, {Deactivated} deactivated",
            country, inserted, updated, unchanged, reactivated, deactivated);

        return new CityImportResult(inserted, updated, unchanged, reactivated, deactivated);
    }

    private static bool HasChanged(City city, PlannedCity p, Guid? parentId) =>
        city.Name != p.Name
        || city.SourceName != p.SourceName
        || city.AlternateNames != p.AlternateNames
        || city.SearchText != p.SearchText
        || city.AdminArea != p.AdminArea
        || city.ParentCityId != parentId
        || city.Latitude != p.Latitude
        || city.Longitude != p.Longitude
        || city.TimeZoneId != p.TimeZoneId
        || city.Population != p.Population;
}

using Sunset.Application.Common;
using Sunset.Domain.Enums;

namespace Sunset.Application.Cities.Import;

/// <summary>A correction of a municipality's display name, keyed by its national (IBGE) code.</summary>
public sealed record CityNameOverride(string NationalCode, string Name, string? AdminArea = null, string? SourceName = null);

/// <summary>A municipality the dataset lacks, added by hand.</summary>
public sealed record ManualCity(string NationalCode, string Name, string AdminArea, double Latitude, double Longitude, string? TimeZoneId, int Population);

/// <summary>
/// A city to write. <see cref="Key"/> identifies a municipality inside the plan and
/// <see cref="ParentKey"/> points a locality at it; the slug is allocated later, against what the
/// database already holds.
/// </summary>
public sealed record PlannedCity(
    CityKind Kind,
    string Source,
    long? ExternalId,
    string Key,
    string? ParentKey,
    string Name,
    string SourceName,
    string? AlternateNames,
    string SearchText,
    string Country,
    string AdminArea,
    string? NationalCode,
    double Latitude,
    double Longitude,
    string? TimeZoneId,
    int Population);

public sealed record CityImportStats(
    int Municipalities,
    int Localities,
    int LocalitiesWithoutMunicipality,
    int LocalitiesSameNameAsMunicipality,
    int ManualAdditions,
    int NameOverridesApplied,
    int MalformedLines);

public sealed record CityImportPlan(
    IReadOnlyList<PlannedCity> Municipalities,
    IReadOnlyList<PlannedCity> Localities,
    CityImportStats Stats);

/// <summary>Turns a GeoNames country dump into the list of cities to store, applying the country's rules.</summary>
public static class CityImportPlanner
{
    public const string GeoNamesSource = "geonames";
    public const string ManualSource = "manual";

    public static CityImportPlan Plan(
        IEnumerable<string> lines,
        CityImportRules rules,
        IReadOnlyCollection<CityNameOverride>? nameOverrides = null,
        IReadOnlyCollection<ManualCity>? manualCities = null)
    {
        var records = new List<GeoNamesRecord>();
        var malformed = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var record = GeoNamesParser.TryParse(line);
            if (record is null)
                malformed++;
            else if (string.Equals(record.CountryCode, rules.Country, StringComparison.OrdinalIgnoreCase))
                records.Add(record);
        }

        return Plan(records, rules, nameOverrides, manualCities, malformed);
    }

    public static CityImportPlan Plan(
        IReadOnlyCollection<GeoNamesRecord> records,
        CityImportRules rules,
        IReadOnlyCollection<CityNameOverride>? nameOverrides = null,
        IReadOnlyCollection<ManualCity>? manualCities = null,
        int malformedLines = 0)
    {
        var overridesByCode = (nameOverrides ?? []).ToDictionary(o => o.NationalCode);
        var overridesApplied = 0;

        // ---- municipalities
        var municipalities = new List<PlannedCity>();
        var municipalityByKey = new Dictionary<string, PlannedCity>();
        var nationalCodes = new HashSet<string>();

        foreach (var r in records.Where(r => rules.MunicipalityFeatureCodes.Contains(r.FeatureCode)).OrderBy(r => r.GeoNameId))
        {
            if (!rules.Admin1ToArea.TryGetValue(r.Admin1Code, out var area))
                continue;

            var nationalCode = rules.NationalCodeIsAdmin2 && !string.IsNullOrWhiteSpace(r.Admin2Code) ? r.Admin2Code : null;
            var name = r.Name;
            if (nationalCode is not null && overridesByCode.TryGetValue(nationalCode, out var fix))
            {
                name = fix.Name;
                overridesApplied++;
            }

            var key = MunicipalityKey(r.Admin1Code, r.Admin2Code);
            if (municipalityByKey.ContainsKey(key))
                continue;

            var city = new PlannedCity(
                CityKind.Municipality, GeoNamesSource, r.GeoNameId, key, null, name, r.Name, Clean(r.AlternateNames),
                BuildSearchText(name, r.Name, r.AlternateNames), rules.Country, area, nationalCode,
                r.Latitude, r.Longitude, r.TimeZoneId, r.Population);

            municipalities.Add(city);
            municipalityByKey[key] = city;
            if (nationalCode is not null)
                nationalCodes.Add(nationalCode);
        }

        // ---- municipalities the dataset lacks
        var manualAdded = 0;
        foreach (var m in manualCities ?? [])
        {
            if (nationalCodes.Contains(m.NationalCode))
                continue;

            var key = "manual:" + m.NationalCode;
            var city = new PlannedCity(
                CityKind.Municipality, ManualSource, null, key, null, m.Name, m.Name, null,
                BuildSearchText(m.Name, m.Name, null), rules.Country, m.AdminArea, m.NationalCode,
                m.Latitude, m.Longitude, m.TimeZoneId, m.Population);

            municipalities.Add(city);
            municipalityByKey[key] = city;
            nationalCodes.Add(m.NationalCode);
            manualAdded++;
        }

        // ---- localities
        var localities = new List<PlannedCity>();
        var withoutParent = 0;
        var sameName = 0;

        foreach (var r in records
                     .Where(r => r.FeatureClass == "P" && rules.LocalityFeatureCodes.Contains(r.FeatureCode))
                     .OrderBy(r => r.GeoNameId))
        {
            if (!rules.Admin1ToArea.TryGetValue(r.Admin1Code, out var area)
                || !municipalityByKey.TryGetValue(MunicipalityKey(r.Admin1Code, r.Admin2Code), out var parent))
            {
                withoutParent++;
                continue;
            }

            // The municipality's own seat shows up as a locality with the same name; its page is the
            // municipality's, so a second "imbituba-imbituba-sc" would only compete with it.
            var normalized = NameNormalizer.Normalize(r.Name);
            if (normalized == NameNormalizer.Normalize(parent.Name) || normalized == NameNormalizer.Normalize(parent.SourceName))
            {
                sameName++;
                continue;
            }

            localities.Add(new PlannedCity(
                CityKind.Locality, GeoNamesSource, r.GeoNameId, "locality:" + r.GeoNameId, parent.Key, r.Name, r.Name,
                Clean(r.AlternateNames), BuildSearchText(r.Name, r.Name, r.AlternateNames), rules.Country, area, null,
                r.Latitude, r.Longitude, r.TimeZoneId, r.Population));
        }

        return new CityImportPlan(
            municipalities,
            localities,
            new CityImportStats(municipalities.Count, localities.Count, withoutParent, sameName, manualAdded, overridesApplied, malformedLines));
    }

    /// <summary>Name, source name and alternate names as one accent-free string for the search index.</summary>
    public static string BuildSearchText(string name, string sourceName, string? alternateNames)
    {
        var words = new List<string>();
        var seen = new HashSet<string>();

        void Add(string? text)
        {
            var normalized = NameNormalizer.Normalize(text);
            if (normalized.Length > 0 && seen.Add(normalized))
                words.Add(normalized);
        }

        Add(name);
        Add(sourceName);
        foreach (var alternate in (alternateNames ?? string.Empty).Split(','))
            Add(alternate);

        return string.Join(' ', words);
    }

    private static string MunicipalityKey(string admin1, string admin2) => $"{admin1}:{admin2}";

    private static string? Clean(string alternateNames) =>
        string.IsNullOrWhiteSpace(alternateNames) ? null : alternateNames.Trim();
}

using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

/// <summary>
/// A place users can attach a location to: a municipality, or a locality inside one. Loaded from a
/// geographic dataset (GeoNames) by the city importer, never typed in by users. <see cref="Slug"/> is
/// assigned once and never changes - it ends up in public URLs.
/// </summary>
public class City : BaseEntity
{
    public CityKind Kind { get; private set; }

    /// <summary>Display name (with accents). For Brazilian municipalities the official IBGE name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>The name exactly as the dataset has it, to tell where a correction was applied.</summary>
    public string SourceName { get; private set; } = null!;

    public string? AlternateNames { get; private set; }

    /// <summary>Accent-free, lower-case words of the name, source name and alternate names: what the search matches against.</summary>
    public string SearchText { get; private set; } = null!;

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; private set; } = null!;

    /// <summary>State/province abbreviation (e.g. SC).</summary>
    public string AdminArea { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    /// <summary>"geonames" or "manual".</summary>
    public string Source { get; private set; } = null!;

    public long? ExternalId { get; private set; }

    /// <summary>In Brazil, the 7-digit IBGE code. Municipalities only.</summary>
    public string? NationalCode { get; private set; }

    /// <summary>Localities only: the municipality they belong to.</summary>
    public Guid? ParentCityId { get; private set; }

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? TimeZoneId { get; private set; }
    public int Population { get; private set; }

    /// <summary>False when the record disappeared from the dataset. Rows are never deleted.</summary>
    public bool IsActive { get; private set; } = true;

    public City? ParentCity { get; private set; }

    private City() { }

    public City(
        CityKind kind, string name, string sourceName, string? alternateNames, string searchText,
        string country, string adminArea, string slug, string source, long? externalId, string? nationalCode,
        Guid? parentCityId, double latitude, double longitude, string? timeZoneId, int population)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(country) || country.Length != 2)
            throw new ArgumentException("Country must be an ISO 3166-1 alpha-2 code.", nameof(country));
        if (string.IsNullOrWhiteSpace(adminArea))
            throw new ArgumentException("AdminArea is required.", nameof(adminArea));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required.", nameof(source));
        if (kind == CityKind.Locality && parentCityId is null)
            throw new ArgumentException("A locality needs a parent municipality.", nameof(parentCityId));
        if (kind == CityKind.Municipality && parentCityId is not null)
            throw new ArgumentException("A municipality cannot have a parent.", nameof(parentCityId));
        ValidateCoordinates(latitude, longitude);

        Kind = kind;
        Name = name;
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? name : sourceName;
        AlternateNames = string.IsNullOrWhiteSpace(alternateNames) ? null : alternateNames;
        SearchText = searchText;
        Country = country.ToUpperInvariant();
        AdminArea = adminArea;
        Slug = slug;
        Source = source;
        ExternalId = externalId;
        NationalCode = nationalCode;
        ParentCityId = parentCityId;
        Latitude = latitude;
        Longitude = longitude;
        TimeZoneId = timeZoneId;
        Population = Math.Max(0, population);
    }

    /// <summary>Applies a newer version of the same record. The slug is deliberately not touched.</summary>
    public void UpdateFromSource(
        string name, string sourceName, string? alternateNames, string searchText, string adminArea,
        Guid? parentCityId, double latitude, double longitude, string? timeZoneId, int population)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (Kind == CityKind.Locality && parentCityId is null)
            throw new ArgumentException("A locality needs a parent municipality.", nameof(parentCityId));
        ValidateCoordinates(latitude, longitude);

        Name = name;
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? name : sourceName;
        AlternateNames = string.IsNullOrWhiteSpace(alternateNames) ? null : alternateNames;
        SearchText = searchText;
        AdminArea = adminArea;
        ParentCityId = Kind == CityKind.Locality ? parentCityId : null;
        Latitude = latitude;
        Longitude = longitude;
        TimeZoneId = timeZoneId;
        Population = Math.Max(0, population);
    }

    /// <summary>
    /// A municipality that was added by hand is now in the dataset: keep the row (and its slug, which may
    /// already be indexed) and attach it to the dataset record.
    /// </summary>
    public void AdoptSource(string source, long externalId)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required.", nameof(source));

        Source = source;
        ExternalId = externalId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
    }
}

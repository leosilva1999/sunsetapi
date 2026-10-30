using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

public class Location : BaseEntity
{
    public string Name { get; private set; } = null!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string City { get; private set; } = null!;
    public decimal AvgRating { get; private set; }

    /// <summary>
    /// Public URL key ("praia-do-rosa-imbituba-sc"). Assigned ONCE when the location is registered and never
    /// changed (it ends up in indexed URLs), not even when a moderator later ties the location to a city or the
    /// location is removed - a deleted location keeps its slug reserved.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>The municipality (always a <see cref="CityKind.Municipality"/>) this location belongs to; null until it is matched.</summary>
    public Guid? CityId { get; private set; }

    /// <summary>The most specific place inside <see cref="CityId"/> (a locality, e.g. a beach village), when known.</summary>
    public Guid? LocalityId { get; private set; }

    /// <summary>Who registered the location. Null for rows that predate the field. Seen by moderation only.</summary>
    public Guid? CreatedByUserId { get; private set; }

    public City? Municipality { get; private set; }
    public City? Locality { get; private set; }

    // Moderator soft delete (same pattern as Photo/Comment): hidden everywhere by the query filter
    // in SunsetDbContext, restorable. Its photos are hidden in the same instant - see
    // ModerationService.DeleteLocationAsync.
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<Rating> Ratings { get; private set; } = new List<Rating>();

    private Location() { }

    public Location(string name, double latitude, double longitude, string city, Guid? createdByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.", nameof(city));
        ValidateCoordinates(latitude, longitude);

        Name = name;
        Latitude = latitude;
        Longitude = longitude;
        City = city;
        AvgRating = 0;
        CreatedByUserId = createdByUserId;
    }

    /// <summary>
    /// Ties the location to a catalog municipality (and optionally a locality inside it) and rewrites the
    /// legacy <see cref="City"/> text to the canonical "{Name} - {State}", so clients and the text search
    /// that still read it stay coherent.
    /// </summary>
    public void ChangeCity(City municipality, City? locality = null)
    {
        ArgumentNullException.ThrowIfNull(municipality);
        if (municipality.Kind != CityKind.Municipality)
            throw new ArgumentException("A location's city must be a municipality.", nameof(municipality));
        if (locality is not null && (locality.Kind != CityKind.Locality || locality.ParentCityId != municipality.Id))
            throw new ArgumentException("The locality must belong to the municipality.", nameof(locality));

        CityId = municipality.Id;
        LocalityId = locality?.Id;
        Municipality = municipality;
        Locality = locality;
        City = CanonicalCityText(municipality);
    }

    public void AssignSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));
        if (!string.IsNullOrEmpty(Slug))
            throw new InvalidOperationException("A location's slug is assigned once and never changes.");

        Slug = slug;
    }

    /// <summary>"Imbituba - SC": the format the legacy text field settles on once a location is linked.</summary>
    public static string CanonicalCityText(City municipality)
    {
        var text = $"{municipality.Name} - {municipality.AdminArea}";
        return text.Length <= 100 ? text : text[..100];
    }

    public void RecalculateAvgRating(decimal avgRating) => AvgRating = avgRating;

    // Moderator correction of a wrong pin; the distance/"near me" queries read these columns live.
    public void ChangeCoordinates(double latitude, double longitude)
    {
        ValidateCoordinates(latitude, longitude);

        Latitude = latitude;
        Longitude = longitude;
    }

    public void SoftDelete(Guid deletedByUserId, DateTime deletedAt)
    {
        DeletedAt = deletedAt;
        DeletedByUserId = deletedByUserId;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedByUserId = null;
    }

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
    }
}

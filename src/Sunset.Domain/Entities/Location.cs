namespace Sunset.Domain.Entities;

public class Location : BaseEntity
{
    public string Name { get; private set; } = null!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string City { get; private set; } = null!;
    public decimal AvgRating { get; private set; }

    // Moderator soft delete (same pattern as Photo/Comment): hidden everywhere by the query filter
    // in SunsetDbContext, restorable. Its photos are hidden in the same instant - see
    // ModerationService.DeleteLocationAsync.
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<Rating> Ratings { get; private set; } = new List<Rating>();

    private Location() { }

    public Location(string name, double latitude, double longitude, string city)
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

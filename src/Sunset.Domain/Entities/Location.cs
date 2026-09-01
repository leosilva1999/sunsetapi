namespace Sunset.Domain.Entities;

public class Location : BaseEntity
{
    public string Name { get; private set; } = null!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string City { get; private set; } = null!;
    public decimal AvgRating { get; private set; }

    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<Rating> Ratings { get; private set; } = new List<Rating>();

    private Location() { }

    public Location(string name, double latitude, double longitude, string city)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.", nameof(city));
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");

        Name = name;
        Latitude = latitude;
        Longitude = longitude;
        City = city;
        AvgRating = 0;
    }

    public void RecalculateAvgRating(decimal avgRating) => AvgRating = avgRating;
}

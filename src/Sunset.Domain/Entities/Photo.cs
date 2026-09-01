namespace Sunset.Domain.Entities;

public class Photo : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid LocationId { get; private set; }
    public string ImageUrl { get; private set; } = null!;
    public string? Caption { get; private set; }
    public int LikesCount { get; private set; }

    public User User { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public ICollection<Like> Likes { get; private set; } = new List<Like>();
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();

    private Photo() { }

    public Photo(Guid userId, Guid locationId, string imageUrl, string? caption)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (locationId == Guid.Empty)
            throw new ArgumentException("LocationId is required.", nameof(locationId));
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("ImageUrl is required.", nameof(imageUrl));

        UserId = userId;
        LocationId = locationId;
        ImageUrl = imageUrl;
        Caption = caption;
        LikesCount = 0;
    }

    public void IncrementLikes() => LikesCount++;

    public void DecrementLikes()
    {
        if (LikesCount > 0)
            LikesCount--;
    }
}

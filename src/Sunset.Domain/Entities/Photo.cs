namespace Sunset.Domain.Entities;

public class Photo : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid LocationId { get; private set; }
    public string ImageUrl { get; private set; } = null!;
    public string? Caption { get; private set; }

    /// <summary>Pixel size as displayed (EXIF rotation applied); null for photos uploaded before it was recorded.</summary>
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public int LikesCount { get; private set; }
    public int CommentsCount { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public User User { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public ICollection<Like> Likes { get; private set; } = new List<Like>();
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();

    private Photo() { }

    public Photo(Guid userId, Guid locationId, string imageUrl, string? caption, int? width = null, int? height = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (locationId == Guid.Empty)
            throw new ArgumentException("LocationId is required.", nameof(locationId));
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("ImageUrl is required.", nameof(imageUrl));
        if (width is <= 0 || height is <= 0 || width.HasValue != height.HasValue)
            throw new ArgumentException("Width and height must be given together and be positive.");

        UserId = userId;
        LocationId = locationId;
        ImageUrl = imageUrl;
        Caption = caption;
        Width = width;
        Height = height;
        LikesCount = 0;
    }

    public void IncrementLikes() => LikesCount++;

    public void DecrementLikes()
    {
        if (LikesCount > 0)
            LikesCount--;
    }

    public void IncrementCommentsCount() => CommentsCount++;

    public void DecrementCommentsCount(int by = 1) => CommentsCount = Math.Max(0, CommentsCount - by);

    public void SoftDelete(Guid deletedByUserId)
    {
        DeletedAt = DateTime.UtcNow;
        DeletedByUserId = deletedByUserId;
    }
}

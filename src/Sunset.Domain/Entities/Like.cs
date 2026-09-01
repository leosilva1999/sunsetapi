namespace Sunset.Domain.Entities;

public class Like : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid PhotoId { get; private set; }

    public User User { get; private set; } = null!;
    public Photo Photo { get; private set; } = null!;

    private Like() { }

    public Like(Guid userId, Guid photoId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (photoId == Guid.Empty)
            throw new ArgumentException("PhotoId is required.", nameof(photoId));

        UserId = userId;
        PhotoId = photoId;
    }
}

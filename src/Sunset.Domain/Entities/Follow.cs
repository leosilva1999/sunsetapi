namespace Sunset.Domain.Entities;

public class Follow : BaseEntity
{
    public Guid FollowerId { get; private set; }
    public Guid FollowingId { get; private set; }

    public User Follower { get; private set; } = null!;
    public User Following { get; private set; } = null!;

    private Follow() { }

    public Follow(Guid followerId, Guid followingId)
    {
        if (followerId == Guid.Empty)
            throw new ArgumentException("FollowerId is required.", nameof(followerId));
        if (followingId == Guid.Empty)
            throw new ArgumentException("FollowingId is required.", nameof(followingId));
        if (followerId == followingId)
            throw new ArgumentException("A user cannot follow themselves.", nameof(followingId));

        FollowerId = followerId;
        FollowingId = followingId;
    }
}

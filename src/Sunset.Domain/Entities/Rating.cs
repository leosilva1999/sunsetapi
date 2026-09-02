namespace Sunset.Domain.Entities;

public class Rating : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid LocationId { get; private set; }
    public int Score { get; private set; }

    public User User { get; private set; } = null!;
    public Location Location { get; private set; } = null!;

    private Rating() { }

    public Rating(Guid userId, Guid locationId, int score)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (locationId == Guid.Empty)
            throw new ArgumentException("LocationId is required.", nameof(locationId));
        if (score is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be between 1 and 5.");

        UserId = userId;
        LocationId = locationId;
        Score = score;
    }

    public void UpdateScore(int score)
    {
        if (score is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be between 1 and 5.");

        Score = score;
    }
}

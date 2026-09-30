namespace Sunset.Domain.Entities;

public class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsActive => UsedAt is null && ExpiresAt > DateTime.UtcNow;

    private PasswordResetToken() { }

    public PasswordResetToken(Guid userId, string tokenHash, DateTime expiresAt)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash is required.", nameof(tokenHash));

        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public void MarkUsed() => UsedAt = DateTime.UtcNow;
}

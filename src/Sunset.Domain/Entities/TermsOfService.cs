namespace Sunset.Domain.Entities;

/// <summary>
/// Each edit inserts a new row instead of updating in place - the current version is simply the
/// one with the highest <see cref="Version"/>, which gives free history with no separate table.
/// </summary>
public class TermsOfService : BaseEntity
{
    public string Content { get; private set; } = null!;
    public int Version { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    public User UpdatedBy { get; private set; } = null!;

    private TermsOfService() { }

    public TermsOfService(string content, int version, Guid updatedByUserId)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must start at 1.");
        if (updatedByUserId == Guid.Empty)
            throw new ArgumentException("UpdatedByUserId is required.", nameof(updatedByUserId));

        Content = content;
        Version = version;
        UpdatedByUserId = updatedByUserId;
    }
}

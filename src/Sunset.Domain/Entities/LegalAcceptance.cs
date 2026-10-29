using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

/// <summary>
/// Evidence that a user accepted a given version of a legal document (Terms of Use or Privacy
/// Policy): who, which document, which version, when (<see cref="BaseEntity.CreatedAt"/>) and how.
/// Append-only - never updated or deleted, not even when the account is anonymized, because it is
/// the proof of what was accepted under which text.
/// </summary>
public class LegalAcceptance : BaseEntity
{
    public Guid UserId { get; private set; }
    public LegalDocumentType DocumentType { get; private set; }
    public int Version { get; private set; }
    public LegalAcceptanceSource Source { get; private set; }

    private LegalAcceptance() { }

    public LegalAcceptance(Guid userId, LegalDocumentType documentType, int version, LegalAcceptanceSource source)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must start at 1.");

        UserId = userId;
        DocumentType = documentType;
        Version = version;
        Source = source;
    }
}

using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

/// <summary>
/// Append-only audit log of moderator/admin actions - written for accountability, no update or
/// delete. No endpoint reads this yet; it exists so the history isn't lost before one is built.
/// </summary>
public class ModerationAction : BaseEntity
{
    public Guid ModeratorId { get; private set; }
    public ModerationActionType ActionType { get; private set; }
    public string TargetDescription { get; private set; } = null!;
    public string? Notes { get; private set; }

    public User Moderator { get; private set; } = null!;

    private ModerationAction() { }

    public ModerationAction(Guid moderatorId, ModerationActionType actionType, string targetDescription, string? notes = null)
    {
        if (moderatorId == Guid.Empty)
            throw new ArgumentException("ModeratorId is required.", nameof(moderatorId));
        if (string.IsNullOrWhiteSpace(targetDescription))
            throw new ArgumentException("TargetDescription is required.", nameof(targetDescription));

        ModeratorId = moderatorId;
        ActionType = actionType;
        TargetDescription = targetDescription;
        Notes = notes;
    }
}

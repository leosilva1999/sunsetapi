using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Moderation;

public static class ModerationActionExtensions
{
    public static ModerationActionResponse ToResponse(this ModerationAction action) =>
        new(
            action.Id,
            action.ModeratorId,
            action.Moderator.Name,
            action.ActionType,
            action.TargetDescription,
            action.Notes,
            action.CreatedAt);
}

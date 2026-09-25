using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

public sealed record ModerationActionResponse(
    Guid Id,
    Guid ModeratorId,
    string ModeratorName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationActionType ActionType,
    string TargetDescription,
    string? Notes,
    DateTime CreatedAt);

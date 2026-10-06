using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] NotificationType Type,
    Guid ActorUserId,
    string ActorName,
    string? ActorAvatarUrl,
    string TargetDescription,
    DateTime? ReadAt,
    DateTime CreatedAt);

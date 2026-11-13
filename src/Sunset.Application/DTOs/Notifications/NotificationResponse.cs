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
    DateTime CreatedAt,
    string? Title = null,
    string? Body = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AnnouncementCategory? Category = null,
    string? Link = null,
    Guid? AnnouncementId = null);

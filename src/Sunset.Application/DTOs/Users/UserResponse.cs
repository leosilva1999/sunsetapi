using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Users;

public sealed record UserResponse(
    Guid Id,
    string Name,
    string Email,
    string? AvatarUrl,
    string? Bio,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] UserRole Role,
    DateTime CreatedAt);

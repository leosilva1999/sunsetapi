using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

public sealed record ChangeUserRoleRequest([property: JsonConverter(typeof(JsonStringEnumConverter))] UserRole Role);

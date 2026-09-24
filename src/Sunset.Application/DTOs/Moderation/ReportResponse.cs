using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

public sealed record ReportResponse(
    Guid Id,
    Guid ReporterId,
    string ReporterName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ReportTargetType TargetType,
    Guid TargetId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ReportReason Reason,
    string? Details,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ReportStatus Status,
    DateTime CreatedAt,
    Guid? ResolvedByUserId,
    DateTime? ResolvedAt);

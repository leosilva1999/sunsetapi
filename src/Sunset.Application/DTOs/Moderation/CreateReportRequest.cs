using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

public sealed record CreateReportRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ReportReason Reason,
    string? Details = null);

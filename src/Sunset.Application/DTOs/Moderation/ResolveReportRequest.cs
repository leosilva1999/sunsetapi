using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

public sealed record ResolveReportRequest([property: JsonConverter(typeof(JsonStringEnumConverter))] ReportStatus Status);

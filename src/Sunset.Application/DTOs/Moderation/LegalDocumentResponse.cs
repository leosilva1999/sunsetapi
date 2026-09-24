using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

public sealed record LegalDocumentResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LegalDocumentType DocumentType,
    string Content,
    int Version,
    DateTime CreatedAt);

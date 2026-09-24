using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Moderation;

public static class LegalDocumentExtensions
{
    public static LegalDocumentResponse ToResponse(this LegalDocument document) =>
        new(document.Id, document.DocumentType, document.Content, document.Version, document.CreatedAt);
}

using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces.Repositories;

public interface ILegalDocumentRepository
{
    Task<LegalDocument?> GetCurrentAsync(LegalDocumentType documentType, CancellationToken cancellationToken = default);
    Task AddAsync(LegalDocument document, CancellationToken cancellationToken = default);
}

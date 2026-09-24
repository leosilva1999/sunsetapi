using Microsoft.EntityFrameworkCore;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class LegalDocumentRepository(SunsetDbContext context) : ILegalDocumentRepository
{
    public Task<LegalDocument?> GetCurrentAsync(LegalDocumentType documentType, CancellationToken cancellationToken = default) =>
        context.LegalDocuments
            .Where(t => t.DocumentType == documentType)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(LegalDocument document, CancellationToken cancellationToken = default)
    {
        await context.LegalDocuments.AddAsync(document, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

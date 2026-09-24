using Microsoft.EntityFrameworkCore;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class TermsOfServiceRepository(SunsetDbContext context) : ITermsOfServiceRepository
{
    public Task<TermsOfService?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        context.TermsOfServiceDocuments
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(TermsOfService termsOfService, CancellationToken cancellationToken = default)
    {
        await context.TermsOfServiceDocuments.AddAsync(termsOfService, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

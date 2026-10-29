using Microsoft.EntityFrameworkCore;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class LegalAcceptanceRepository(SunsetDbContext context) : ILegalAcceptanceRepository
{
    public async Task<IReadOnlyList<LegalAcceptance>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.LegalAcceptances.AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task AddRangeAsync(IEnumerable<LegalAcceptance> acceptances, CancellationToken cancellationToken = default)
    {
        await context.LegalAcceptances.AddRangeAsync(acceptances, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

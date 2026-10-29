using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface ILegalAcceptanceRepository
{
    /// <summary>Every acceptance of one user (a handful of rows: one per document version accepted).</summary>
    Task<IReadOnlyList<LegalAcceptance>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<LegalAcceptance> acceptances, CancellationToken cancellationToken = default);
}

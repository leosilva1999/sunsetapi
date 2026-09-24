using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface ITermsOfServiceRepository
{
    Task<TermsOfService?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TermsOfService termsOfService, CancellationToken cancellationToken = default);
}

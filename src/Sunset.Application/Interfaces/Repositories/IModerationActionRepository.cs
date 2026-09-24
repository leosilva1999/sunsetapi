using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface IModerationActionRepository
{
    Task AddAsync(ModerationAction action, CancellationToken cancellationToken = default);
}

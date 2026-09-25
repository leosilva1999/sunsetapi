using Sunset.Application.Common;
using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface IModerationActionRepository
{
    Task AddAsync(ModerationAction action, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<ModerationAction>> GetAllAsync(string? cursor, int limit, CancellationToken cancellationToken = default);
}

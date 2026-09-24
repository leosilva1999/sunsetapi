using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class ModerationActionRepository(SunsetDbContext context) : IModerationActionRepository
{
    public async Task AddAsync(ModerationAction action, CancellationToken cancellationToken = default)
    {
        await context.ModerationActions.AddAsync(action, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

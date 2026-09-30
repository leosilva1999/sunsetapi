using Microsoft.EntityFrameworkCore;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class PasswordResetTokenRepository(SunsetDbContext context) : IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
    {
        await context.PasswordResetTokens.AddAsync(token, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

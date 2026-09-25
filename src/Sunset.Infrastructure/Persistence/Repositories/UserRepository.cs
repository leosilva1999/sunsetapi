using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class UserRepository(SunsetDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await context.Users.AddAsync(user, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task<CursorPagedResult<User>> SearchAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var users = context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
            users = users.Where(u => EF.Functions.Like(u.Name, $"%{query}%") || EF.Functions.Like(u.Email, $"%{query}%"));

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            users = users.Where(u =>
                u.CreatedAt < c.CreatedAt ||
                (u.CreatedAt == c.CreatedAt && u.Id.CompareTo(c.Id) < 0));
        }

        var items = await users
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<User>(page, nextCursor, hasMore);
    }
}

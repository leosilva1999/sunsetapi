using Sunset.Application.Common;
using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    // q opcional - sem ele, lista todo mundo mais recente primeiro (útil pra promover o
    // primeiro Admin/Moderator num ambiente sem um pra começar). Com q, casa contra
    // Name OU Email (LIKE '%q%', mesma semântica de substring do fallback <3 chars da
    // busca de locations - não tem índice FULLTEXT aqui, a tabela de usuários não deve
    // crescer a ponto de precisar).
    Task<CursorPagedResult<User>> SearchAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken = default);
}

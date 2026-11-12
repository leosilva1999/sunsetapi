using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces.Repositories;

public interface IPushTicketRepository
{
    Task AddRangeAsync(IReadOnlyCollection<PushTicket> tickets, CancellationToken cancellationToken = default);

    /// <summary>Tickets created before <paramref name="createdBefore"/>, oldest first.</summary>
    Task<IReadOnlyList<PushTicket>> GetDueAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default);

    Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

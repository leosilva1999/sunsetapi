using Sunset.Application.Common;
using Sunset.Application.DTOs.Announcements;

namespace Sunset.Application.Interfaces;

public interface IAnnouncementService
{
    /// <summary>How many people would receive it, without sending anything.</summary>
    Task<AnnouncementPreviewResponse> PreviewAsync(bool isAdmin, PreviewAnnouncementRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the announcement and hands it to the delivery worker (answered before anyone is notified). Only an admin may
    /// address more than a list of accounts; a moderator is limited to up to 20 named accounts. The audience is counted again
    /// and must still be <c>ExpectedRecipients</c>, else 409. At most 5 mass announcements per day.
    /// </summary>
    Task<AnnouncementResponse> CreateAsync(Guid senderId, bool isAdmin, CreateAnnouncementRequest request, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<AnnouncementResponse>> ListAsync(string? cursor, int limit, CancellationToken cancellationToken = default);

    Task<AnnouncementResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

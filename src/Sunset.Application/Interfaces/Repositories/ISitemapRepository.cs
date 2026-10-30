using Sunset.Application.Common;
using Sunset.Application.DTOs.Sitemap;

namespace Sunset.Application.Interfaces.Repositories;

public interface ISitemapRepository
{
    Task<CursorPagedResult<SitemapEntryResponse>> GetCitiesAsync(string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<SitemapEntryResponse>> GetLocationsAsync(string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<SitemapEntryResponse>> GetPhotosAsync(string? cursor, int limit, CancellationToken cancellationToken = default);
}

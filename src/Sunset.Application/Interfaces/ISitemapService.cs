using Sunset.Application.Common;
using Sunset.Application.DTOs.Sitemap;

namespace Sunset.Application.Interfaces;

/// <summary>A lean listing of everything that has a public page, for the site's sitemap.</summary>
public interface ISitemapService
{
    /// <summary>
    /// Entries ordered by id (a stable order, so paging survives new rows). Only visible things: municipalities that
    /// have at least one visible location, visible locations, visible photos.
    /// </summary>
    Task<CursorPagedResult<SitemapEntryResponse>> GetEntriesAsync(SitemapEntryType type, string? cursor, int limit, CancellationToken cancellationToken = default);
}

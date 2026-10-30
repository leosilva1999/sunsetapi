using Sunset.Application.Common;
using Sunset.Application.DTOs.Sitemap;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;

namespace Sunset.Application.Services;

public class SitemapService(ISitemapRepository sitemapRepository) : ISitemapService
{
    public const int MaxLimit = 1000;

    public Task<CursorPagedResult<SitemapEntryResponse>> GetEntriesAsync(SitemapEntryType type, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(limit, 1, MaxLimit);

        return type switch
        {
            SitemapEntryType.City => sitemapRepository.GetCitiesAsync(cursor, size, cancellationToken),
            SitemapEntryType.Location => sitemapRepository.GetLocationsAsync(cursor, size, cancellationToken),
            SitemapEntryType.Photo => sitemapRepository.GetPhotosAsync(cursor, size, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown sitemap entry type."),
        };
    }
}

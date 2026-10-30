using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Sunset.API.Caching;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Sitemap;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/sitemap")]
public class SitemapController(ISitemapService sitemapService) : ControllerBase
{
    // Not rate limited as a search: this is how the site's sitemap walks everything, page after page.
    [OutputCache(PolicyName = PublicCaching.SitemapPolicy)]
    [HttpGet("entries")]
    public async Task<ActionResult<CursorPagedResult<SitemapEntryResponse>>> Entries(
        [FromQuery] SitemapEntryType type,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(type))
            return BadRequest(new { title = "Invalid type.", errors = new Dictionary<string, string[]> { ["type"] = ["Use city, location or photo."] } });

        return Ok(await sitemapService.GetEntriesAsync(type, cursor, limit, cancellationToken));
    }
}

using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/cities")]
public class CitiesController(
    ICityService cityService,
    IValidator<CityListQuery> listQueryValidator) : ControllerBase
{
    // A text search runs a full-text query per call (and the site's autocomplete fires it on every
    // keystroke), so it shares the location search's per-client limit.
    [EnableRateLimiting("Search")]
    [HttpGet]
    public async Task<ActionResult<CursorPagedResult<CityResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] CityKindFilter kind = CityKindFilter.Municipality,
        [FromQuery] string? country = null,
        [FromQuery] string? state = null,
        [FromQuery] CitySortOption sort = CitySortOption.Population,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new CityListQuery(q, kind, country, state, sort, cursor, Math.Clamp(limit, 1, 50));
        await listQueryValidator.ValidateAndThrowAsync(query, cancellationToken);

        return Ok(await cityService.SearchAsync(query, cancellationToken));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CityResponse>> GetBySlug(string slug, CancellationToken cancellationToken) =>
        Ok(await cityService.GetBySlugAsync(slug.Trim().ToLowerInvariant(), cancellationToken));
}

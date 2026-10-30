using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Sunset.API.Caching;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/cities")]
public class CitiesController(
    ICityService cityService,
    IValidator<CityListQuery> listQueryValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    // A text search runs a full-text query per call (and the site's autocomplete fires it on every
    // keystroke), so it shares the location search's per-client limit.
    [EnableRateLimiting("CitySearch")]
    [OutputCache(PolicyName = PublicCaching.CitiesPolicy)]
    [HttpGet]
    public async Task<ActionResult<CursorPagedResult<CityResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] CityKindFilter kind = CityKindFilter.Municipality,
        [FromQuery] string? country = null,
        [FromQuery] string? state = null,
        [FromQuery] CitySortOption sort = CitySortOption.Locations,
        [FromQuery] bool includeEmpty = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new CityListQuery(q, kind, country, state, sort, cursor, Math.Clamp(limit, 1, 50), includeEmpty);
        await listQueryValidator.ValidateAndThrowAsync(query, cancellationToken);

        return Ok(await cityService.SearchAsync(query, cancellationToken));
    }

    [OutputCache(PolicyName = PublicCaching.CitiesPolicy)]
    [HttpGet("{slug}")]
    public async Task<ActionResult<CityDetailResponse>> GetBySlug(string slug, CancellationToken cancellationToken) =>
        Ok(await cityService.GetBySlugAsync(Normalize(slug), cancellationToken));

    [OutputCache(PolicyName = PublicCaching.CitiesPolicy)]
    [HttpGet("{slug}/locations")]
    public async Task<ActionResult<CursorPagedResult<LocationResponse>>> GetLocations(
        string slug,
        [FromQuery] CityLocationSort sort = CityLocationSort.Rating,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await cityService.GetLocationsAsync(Normalize(slug), sort, cursor, Math.Clamp(limit, 1, 50), cancellationToken));

    // Reads the token when one is sent, so likedByCurrentUser is right for a logged-in visitor (anonymous: false).
    [OutputCache(PolicyName = PublicCaching.CitiesPolicy)]
    [HttpGet("{slug}/photos")]
    public async Task<ActionResult<CursorPagedResult<PhotoResponse>>> GetPhotos(
        string slug,
        [FromQuery] PhotoSortOption sort = PhotoSortOption.Top,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await cityService.GetPhotosAsync(Normalize(slug), sort, cursor, Math.Clamp(limit, 1, 50), currentUserService.UserId, cancellationToken));

    [OutputCache(PolicyName = PublicCaching.CitiesPolicy)]
    [HttpGet("{slug}/sunset")]
    public async Task<ActionResult<SunsetTimeResponse>> GetSunset(
        string slug,
        [FromQuery] DateOnly? date = null,
        CancellationToken cancellationToken = default) =>
        Ok(await cityService.GetSunsetTimeAsync(Normalize(slug), date, cancellationToken));

    private static string Normalize(string slug) => slug.Trim().ToLowerInvariant();
}

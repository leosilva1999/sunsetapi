using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Sunset.API.Caching;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/geo")]
public class GeoController(ICityService cityService, IValidator<NearbyCitiesQuery> validator) : ControllerBase
{
    // Fired as the pin moves, so it shares the search limit (lat/lng make it a "search" for the limiter).
    [EnableRateLimiting("Search")]
    [OutputCache(PolicyName = PublicCaching.CitiesPolicy)]
    [HttpGet("cities")]
    public async Task<ActionResult<NearbyCitiesResponse>> NearbyCities(
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] Guid? cityId = null,
        [FromQuery] int limit = NearbyCitiesQuery.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var query = new NearbyCitiesQuery(lat, lng, cityId, Math.Clamp(limit, 1, NearbyCitiesQuery.MaxLimit));
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return Ok(await cityService.GetNearbyAsync(query, cancellationToken));
    }
}

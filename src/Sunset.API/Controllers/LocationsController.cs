using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/locations")]
public class LocationsController(
    ILocationService locationService,
    IValidator<CreateLocationRequest> createLocationValidator,
    IValidator<CreateRatingRequest> createRatingValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CursorPagedResult<LocationResponse>>> Search(
        [FromQuery] string? q,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? radius,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new LocationSearchQuery(q, lat, lng, radius, cursor, Math.Clamp(limit, 1, 50));
        var page = await locationService.SearchAsync(query, cancellationToken);
        return Ok(page);
    }

    [HttpGet("ranking")]
    public async Task<ActionResult<IReadOnlyList<LocationResponse>>> GetRanking(
        [FromQuery] RankingPeriod period = RankingPeriod.All,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var ranking = await locationService.GetRankingAsync(period, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(ranking);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LocationResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await locationService.GetByIdAsync(id, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<LocationResponse>> Create(CreateLocationRequest request, CancellationToken cancellationToken)
    {
        await createLocationValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await locationService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet("{id:guid}/photos")]
    public async Task<ActionResult<CursorPagedResult<PhotoResponse>>> GetPhotos(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await locationService.GetPhotosAsync(id, cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    [HttpGet("{id:guid}/sunset")]
    public async Task<ActionResult<SunsetTimeResponse>> GetSunset(Guid id, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var response = await locationService.GetSunsetTimeAsync(id, date, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPost("{id:guid}/ratings")]
    public async Task<ActionResult<LocationResponse>> Rate(Guid id, CreateRatingRequest request, CancellationToken cancellationToken)
    {
        await createRatingValidator.ValidateAndThrowAsync(request, cancellationToken);

        var userId = currentUserService.UserId
            ?? throw new UnauthorizedActionException("User is not authenticated.");

        var response = await locationService.RateAsync(userId, id, request, cancellationToken);
        return Ok(response);
    }
}

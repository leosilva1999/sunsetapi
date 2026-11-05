using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.DTOs.Stats;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

/// <summary>Numbers for the moderation dashboard. Staff only; not public and not cached (a signed-in request never is).</summary>
[ApiController]
[Route("api/v1/moderation/stats")]
[Authorize(Roles = "Moderator,Admin")]
public class StatsController(IStatsService statsService) : ControllerBase
{
    [HttpGet("growth")]
    public async Task<ActionResult<GrowthResponse>> GetGrowth(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] GrowthGranularity granularity = GrowthGranularity.Day,
        [FromQuery] string? metrics = null,
        CancellationToken cancellationToken = default) =>
        Ok(await statsService.GetGrowthAsync(new GrowthQuery(from, to, granularity, metrics), cancellationToken));
}

using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/moderation")]
[Authorize(Roles = "Moderator,Admin")]
public class ModerationController(
    IModerationService moderationService,
    IValidator<ResolveReportRequest> resolveReportValidator,
    IValidator<ChangeUserRoleRequest> changeUserRoleValidator,
    IValidator<BanUserRequest> banUserValidator,
    IValidator<AnonymizeUserRequest> anonymizeUserValidator,
    IValidator<UpdateLocationCoordinatesRequest> updateCoordinatesValidator,
    IValidator<MergeLocationsRequest> mergeLocationsValidator,
    IValidator<ChangeLocationCityRequest> changeLocationCityValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("reports")]
    public async Task<ActionResult<CursorPagedResult<ReportResponse>>> GetReports(
        [FromQuery] ReportStatus status = ReportStatus.Pending,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await moderationService.GetReportsAsync(status, cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    [HttpPatch("reports/{id:guid}")]
    public async Task<ActionResult<ReportResponse>> ResolveReport(Guid id, ResolveReportRequest request, CancellationToken cancellationToken)
    {
        await resolveReportValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await moderationService.ResolveReportAsync(RequireUserId(), id, request, cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("users/{id:guid}/role")]
    public async Task<ActionResult<UserResponse>> ChangeUserRole(Guid id, ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        await changeUserRoleValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await moderationService.ChangeUserRoleAsync(RequireUserId(), id, request, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("locations/{id:guid}")]
    public async Task<IActionResult> DeleteLocation(Guid id, CancellationToken cancellationToken)
    {
        await moderationService.DeleteLocationAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpPost("locations/{id:guid}/restore")]
    public async Task<IActionResult> RestoreLocation(Guid id, CancellationToken cancellationToken)
    {
        await moderationService.RestoreLocationAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpGet("locations/deleted")]
    public async Task<ActionResult<IReadOnlyList<DeletedLocationResponse>>> GetDeletedLocations(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var locations = await moderationService.GetDeletedLocationsAsync(Math.Clamp(limit, 1, 100), cancellationToken);
        return Ok(locations);
    }

    [HttpPost("locations/{id:guid}/merge")]
    public async Task<ActionResult<LocationResponse>> MergeLocations(Guid id, MergeLocationsRequest request, CancellationToken cancellationToken)
    {
        await mergeLocationsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await moderationService.MergeLocationsAsync(RequireUserId(), id, request, cancellationToken);
        return Ok(response);
    }

    // Locations that no catalog city could be matched to, with what the system worked out about each.
    [HttpGet("locations/unresolved-city")]
    public async Task<ActionResult<IReadOnlyList<UnresolvedCityLocationResponse>>> GetUnresolvedCityLocations(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await moderationService.GetUnresolvedCityLocationsAsync(Math.Clamp(limit, 1, 50), cancellationToken));

    [HttpPatch("locations/{id:guid}/city")]
    public async Task<ActionResult<LocationResponse>> ChangeLocationCity(Guid id, ChangeLocationCityRequest request, CancellationToken cancellationToken)
    {
        await changeLocationCityValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await moderationService.ChangeLocationCityAsync(RequireUserId(), id, request, cancellationToken);
        return Ok(response);
    }

    [HttpPatch("locations/{id:guid}/coordinates")]
    public async Task<ActionResult<LocationResponse>> UpdateLocationCoordinates(Guid id, UpdateLocationCoordinatesRequest request, CancellationToken cancellationToken)
    {
        await updateCoordinatesValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await moderationService.UpdateLocationCoordinatesAsync(RequireUserId(), id, request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("users/{id:guid}/ban")]
    public async Task<IActionResult> BanUser(Guid id, BanUserRequest request, CancellationToken cancellationToken)
    {
        await banUserValidator.ValidateAndThrowAsync(request, cancellationToken);
        await moderationService.BanUserAsync(RequireUserId(), id, request, cancellationToken);
        return NoContent();
    }

    // Admin-only: this erases the account's personal data and cannot be undone.
    [Authorize(Roles = "Admin")]
    [HttpPost("users/{id:guid}/anonymize")]
    public async Task<IActionResult> AnonymizeUser(Guid id, AnonymizeUserRequest request, CancellationToken cancellationToken)
    {
        await anonymizeUserValidator.ValidateAndThrowAsync(request, cancellationToken);
        await moderationService.AnonymizeUserAsync(RequireUserId(), id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("users/{id:guid}/ban")]
    public async Task<IActionResult> UnbanUser(Guid id, CancellationToken cancellationToken)
    {
        await moderationService.UnbanUserAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    // Admin-only (não Moderator, ao contrário do resto da classe) - hoje só serve pra
    // achar o usuário certo antes de promover/rebaixar via PATCH .../role acima, que já
    // é Admin-only; devolve email (como o PATCH .../role já faz), não tem sentido abrir
    // isso pra Moderator.
    [Authorize(Roles = "Admin")]
    [HttpGet("users")]
    public async Task<ActionResult<CursorPagedResult<UserResponse>>> SearchUsers(
        [FromQuery] string? q = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await moderationService.SearchUsersAsync(q, cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    [HttpGet("actions")]
    public async Task<ActionResult<CursorPagedResult<ModerationActionResponse>>> GetActions(
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await moderationService.GetActionsAsync(cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    private Guid RequireUserId() =>
        currentUserService.UserId ?? throw new UnauthorizedActionException("User is not authenticated.");
}

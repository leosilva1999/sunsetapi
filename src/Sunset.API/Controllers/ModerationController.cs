using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
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

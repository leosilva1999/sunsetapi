using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Announcements;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

/// <summary>Messages from the team to the app's users (service notices, promotions). Staff only.</summary>
[ApiController]
[Route("api/v1/moderation/announcements")]
[Authorize(Roles = "Moderator,Admin")]
public class AnnouncementsController(IAnnouncementService announcementService, ICurrentUserService currentUserService) : ControllerBase
{
    private Guid RequireUserId() => currentUserService.UserId ?? throw new UnauthorizedActionException("User is not authenticated.");

    private bool IsAdmin => currentUserService.Role == UserRole.Admin;

    [HttpPost("preview")]
    public async Task<ActionResult<AnnouncementPreviewResponse>> Preview(PreviewAnnouncementRequest request, CancellationToken cancellationToken) =>
        Ok(await announcementService.PreviewAsync(IsAdmin, request, cancellationToken));

    // 202: it is saved and queued; the worker notifies the recipients in the next seconds. Follow it with GET.
    [HttpPost]
    public async Task<IActionResult> Create(CreateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var created = await announcementService.CreateAsync(RequireUserId(), IsAdmin, request, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<CursorPagedResult<AnnouncementResponse>>> List(
        [FromQuery] string? cursor, [FromQuery] int limit = 20, CancellationToken cancellationToken = default) =>
        Ok(await announcementService.ListAsync(cursor, Math.Clamp(limit, 1, 50), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnnouncementResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await announcementService.GetAsync(id, cancellationToken));
}

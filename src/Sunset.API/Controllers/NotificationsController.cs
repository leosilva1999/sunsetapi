using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController(
    INotificationService notificationService,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CursorPagedResult<NotificationResponse>>> GetAll(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await notificationService.GetNotificationsAsync(RequireUserId(), cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<NotificationUnreadCountResponse>> GetUnreadCount(CancellationToken cancellationToken)
    {
        var response = await notificationService.GetUnreadCountAsync(RequireUserId(), cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await notificationService.MarkReadAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await notificationService.MarkAllReadAsync(RequireUserId(), cancellationToken);
        return NoContent();
    }

    private Guid RequireUserId() =>
        currentUserService.UserId ?? throw new UnauthorizedActionException("User is not authenticated.");
}

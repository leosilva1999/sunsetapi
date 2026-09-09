using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/comments")]
public class CommentsController(IPhotoService photoService, ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("{id:guid}/replies")]
    public async Task<ActionResult<CursorPagedResult<CommentResponse>>> GetReplies(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await photoService.GetRepliesAsync(id, cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new UnauthorizedActionException("User is not authenticated.");

        await photoService.DeleteCommentAsync(userId, id, cancellationToken);
        return NoContent();
    }
}

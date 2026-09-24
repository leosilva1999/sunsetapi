using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/comments")]
public class CommentsController(
    IPhotoService photoService,
    IModerationService moderationService,
    IValidator<CreateReportRequest> createReportValidator,
    ICurrentUserService currentUserService) : ControllerBase
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

        await photoService.DeleteCommentAsync(userId, id, currentUserService.IsModerator, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [EnableRateLimiting("Reports")]
    [HttpPost("{id:guid}/reports")]
    public async Task<ActionResult<ReportResponse>> Report(Guid id, CreateReportRequest request, CancellationToken cancellationToken)
    {
        await createReportValidator.ValidateAndThrowAsync(request, cancellationToken);

        var userId = currentUserService.UserId
            ?? throw new UnauthorizedActionException("User is not authenticated.");

        var response = await moderationService.CreateReportAsync(userId, ReportTargetType.Comment, id, request, cancellationToken);
        return Ok(response);
    }
}

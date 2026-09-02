using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/photos")]
public class PhotosController(
    IPhotoService photoService,
    IValidator<CreatePhotoRequest> createPhotoValidator,
    IValidator<CreateCommentRequest> createCommentValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CursorPagedResult<PhotoResponse>>> GetFeed(
        [FromQuery] PhotoSortOption sort = PhotoSortOption.Recent,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await photoService.GetFeedAsync(sort, cursor, Math.Clamp(limit, 1, 50), currentUserService.UserId, cancellationToken);
        return Ok(page);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<PhotoResponse>> Create(CreatePhotoRequest request, CancellationToken cancellationToken)
    {
        await createPhotoValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = RequireUserId();

        var response = await photoService.CreateAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PhotoResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await photoService.GetByIdAsync(id, currentUserService.UserId, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await photoService.DeleteAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("{id:guid}/likes")]
    public async Task<IActionResult> Like(Guid id, CancellationToken cancellationToken)
    {
        await photoService.LikeAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id:guid}/likes")]
    public async Task<IActionResult> Unlike(Guid id, CancellationToken cancellationToken)
    {
        await photoService.UnlikeAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<CursorPagedResult<CommentResponse>>> GetComments(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await photoService.GetCommentsAsync(id, cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }

    [Authorize]
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentResponse>> AddComment(Guid id, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        await createCommentValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await photoService.AddCommentAsync(RequireUserId(), id, request, cancellationToken);
        return Ok(response);
    }

    private Guid RequireUserId() =>
        currentUserService.UserId ?? throw new UnauthorizedActionException("User is not authenticated.");
}

using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController(
    IUserService userService,
    IValidator<UpdateProfileRequest> updateProfileValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await userService.GetByIdAsync(id, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpPatch("me")]
    public async Task<ActionResult<UserResponse>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        await updateProfileValidator.ValidateAndThrowAsync(request, cancellationToken);

        var userId = currentUserService.UserId
            ?? throw new UnauthorizedActionException("User is not authenticated.");

        var response = await userService.UpdateProfileAsync(userId, request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}/photos")]
    public async Task<ActionResult<CursorPagedResult<PhotoResponse>>> GetPhotos(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await userService.GetPhotosAsync(id, cursor, Math.Clamp(limit, 1, 50), cancellationToken);
        return Ok(page);
    }
}

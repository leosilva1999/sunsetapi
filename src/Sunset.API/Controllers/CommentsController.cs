using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/comments")]
public class CommentsController(IPhotoService photoService, ICurrentUserService currentUserService) : ControllerBase
{
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

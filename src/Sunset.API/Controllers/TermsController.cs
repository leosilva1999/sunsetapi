using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/terms")]
public class TermsController(
    IModerationService moderationService,
    IValidator<UpdateTermsOfServiceRequest> updateTermsValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TermsOfServiceResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        var response = await moderationService.GetCurrentTermsAsync(cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public async Task<ActionResult<TermsOfServiceResponse>> Update(UpdateTermsOfServiceRequest request, CancellationToken cancellationToken)
    {
        await updateTermsValidator.ValidateAndThrowAsync(request, cancellationToken);

        var adminId = currentUserService.UserId
            ?? throw new UnauthorizedActionException("User is not authenticated.");

        var response = await moderationService.UpdateTermsAsync(adminId, request, cancellationToken);
        return Ok(response);
    }
}

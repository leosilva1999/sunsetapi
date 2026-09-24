using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Domain.Enums;

namespace Sunset.API.Controllers;

[ApiController]
[Route("api/v1/privacy")]
public class PrivacyController(
    IModerationService moderationService,
    IValidator<UpdateLegalDocumentRequest> updateDocumentValidator,
    ICurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LegalDocumentResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        var response = await moderationService.GetCurrentLegalDocumentAsync(LegalDocumentType.PrivacyPolicy, cancellationToken);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public async Task<ActionResult<LegalDocumentResponse>> Update(UpdateLegalDocumentRequest request, CancellationToken cancellationToken)
    {
        await updateDocumentValidator.ValidateAndThrowAsync(request, cancellationToken);

        var adminId = currentUserService.UserId
            ?? throw new UnauthorizedActionException("User is not authenticated.");

        var response = await moderationService.UpdateLegalDocumentAsync(adminId, LegalDocumentType.PrivacyPolicy, request, cancellationToken);
        return Ok(response);
    }
}

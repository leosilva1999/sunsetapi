using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Moderation;

public static class TermsOfServiceExtensions
{
    public static TermsOfServiceResponse ToResponse(this TermsOfService termsOfService) =>
        new(termsOfService.Id, termsOfService.Content, termsOfService.Version, termsOfService.CreatedAt);
}

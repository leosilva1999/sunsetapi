namespace Sunset.Application.DTOs.Moderation;

public sealed record TermsOfServiceResponse(Guid Id, string Content, int Version, DateTime CreatedAt);

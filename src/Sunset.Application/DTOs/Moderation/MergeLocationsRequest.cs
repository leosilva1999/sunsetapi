namespace Sunset.Application.DTOs.Moderation;

/// <param name="TargetId">The location that stays and receives the photos and ratings of the one in the URL.</param>
public sealed record MergeLocationsRequest(Guid TargetId);

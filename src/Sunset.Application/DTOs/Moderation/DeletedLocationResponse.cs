namespace Sunset.Application.DTOs.Moderation;

public sealed record DeletedLocationResponse(
    Guid Id,
    string Name,
    string City,
    double Latitude,
    double Longitude,
    DateTime DeletedAt,
    Guid? DeletedByUserId);

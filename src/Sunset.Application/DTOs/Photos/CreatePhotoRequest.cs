namespace Sunset.Application.DTOs.Photos;

public sealed record CreatePhotoRequest(Guid LocationId, string ImageUrl, string? Caption);

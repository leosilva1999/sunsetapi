namespace Sunset.Application.Interfaces;

/// <summary>
/// Cleans an image the client has just uploaded straight to storage (pre-signed PUT): strips
/// EXIF/GPS and other metadata and rewrites the object in place, so what gets published never
/// exposes where or with what the photo was taken. Also verifies it is really a JPEG/PNG.
/// </summary>
public interface IUploadedImageSanitizer
{
    /// <returns>true when the object is a valid, now-clean image; false when the URL is outside our
    /// storage, the object doesn't exist, is too large, or isn't a well-formed JPEG/PNG.</returns>
    /// <exception cref="Sunset.Application.Exceptions.ExternalServiceException">Storage couldn't be reached.</exception>
    Task<bool> SanitizeAsync(string imageUrl, CancellationToken cancellationToken = default);
}

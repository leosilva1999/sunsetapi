using Sunset.Application.Common;

namespace Sunset.Application.DTOs.Users;

public sealed record UpdateProfileRequest(Optional<string> Name, Optional<string?> AvatarUrl, Optional<string?> Bio);

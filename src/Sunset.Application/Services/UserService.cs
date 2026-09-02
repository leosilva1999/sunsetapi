using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;

namespace Sunset.Application.Services;

public class UserService(IUserRepository userRepository, IPhotoRepository photoRepository) : IUserService
{
    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        return user.ToResponse();
    }

    public async Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        user.UpdateProfile(request.Name, request.AvatarUrl);
        await userRepository.SaveChangesAsync(cancellationToken);

        return user.ToResponse();
    }

    public async Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
            throw new NotFoundException("User not found.");

        var page = await photoRepository.GetByUserIdAsync(userId, cursor, limit, cancellationToken);
        var items = page.Items.Select(p => p.ToResponse()).ToList();

        return new CursorPagedResult<PhotoResponse>(items, page.NextCursor, page.HasMore);
    }
}

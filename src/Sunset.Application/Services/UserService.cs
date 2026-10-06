using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class UserService(
    IUserRepository userRepository,
    IPhotoRepository photoRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IFollowRepository followRepository,
    INotificationRepository notificationRepository,
    IPasswordHasher passwordHasher) : IUserService
{
    public async Task<PublicUserResponse> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var isFollowed = currentUserId is { } viewerId && await followRepository.GetAsync(viewerId, id, cancellationToken) is not null;
        return user.ToPublicResponse(isFollowed);
    }

    public async Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var name = request.Name.IsSet ? request.Name.Value! : user.Name;
        var avatarUrl = request.AvatarUrl.IsSet ? request.AvatarUrl.Value : user.AvatarUrl;
        var bio = request.Bio.IsSet ? request.Bio.Value : user.Bio;

        user.UpdateProfile(name, avatarUrl, bio);
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

    public async Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var placeholderEmail = $"deleted-{userId:N}@sunset.invalid";
        var unusablePasswordHash = passwordHasher.Hash(Guid.NewGuid().ToString());
        user.Anonymize(placeholderEmail, unusablePasswordHash);

        await refreshTokenRepository.RevokeAllForUserAsync(userId, cancellationToken);
        await userRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task FollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken = default)
    {
        if (followerId == followingId)
            throw new ConflictException("A user cannot follow themselves.");

        var follower = await userRepository.GetByIdAsync(followerId, cancellationToken)
            ?? throw new NotFoundException("User not found.");
        var following = await userRepository.GetByIdAsync(followingId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (await followRepository.GetAsync(followerId, followingId, cancellationToken) is not null)
            return;

        await followRepository.AddAsync(new Follow(followerId, followingId), cancellationToken);

        follower.IncrementFollowingCount();
        following.IncrementFollowersCount();
        await userRepository.SaveChangesAsync(cancellationToken);

        await notificationRepository.AddAsync(
            new Notification(followingId, NotificationType.NewFollower, followerId, $"User:{followerId}"),
            cancellationToken);
    }

    public async Task UnfollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken = default)
    {
        var follow = await followRepository.GetAsync(followerId, followingId, cancellationToken);
        if (follow is null)
            return;

        var follower = await userRepository.GetByIdAsync(followerId, cancellationToken)
            ?? throw new NotFoundException("User not found.");
        var following = await userRepository.GetByIdAsync(followingId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        await followRepository.RemoveAsync(follow, cancellationToken);

        follower.DecrementFollowingCount();
        following.DecrementFollowersCount();
        await userRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPagedResult<PublicUserResponse>> GetFollowersAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
            throw new NotFoundException("User not found.");

        var page = await followRepository.GetFollowersAsync(userId, cursor, limit, cancellationToken);
        var items = page.Items.Select(u => u.ToPublicResponse()).ToList();

        return new CursorPagedResult<PublicUserResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<CursorPagedResult<PublicUserResponse>> GetFollowingAsync(Guid userId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
            throw new NotFoundException("User not found.");

        var page = await followRepository.GetFollowingAsync(userId, cursor, limit, cancellationToken);
        var items = page.Items.Select(u => u.ToPublicResponse()).ToList();

        return new CursorPagedResult<PublicUserResponse>(items, page.NextCursor, page.HasMore);
    }
}

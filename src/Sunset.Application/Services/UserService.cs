using FluentValidation;
using FluentValidation.Results;
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
    IPasswordHasher passwordHasher,
    IEmailSender emailSender) : IUserService
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

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (!user.HasPassword)
            throw new ConflictException("This account has no password (it signs in with Google). Use \"forgot password\" to set one.");

        // Validation errors (400), not 401: a wrong current password is a form mistake, and a 401
        // would make clients treat it as an expired session and try to refresh/sign out.
        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ValidationException([new ValidationFailure(nameof(request.CurrentPassword), "Current password is incorrect.")]);

        if (passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ValidationException([new ValidationFailure(nameof(request.NewPassword), "New password must be different from the current password.")]);

        user.ChangePassword(passwordHasher.Hash(request.NewPassword));
        await userRepository.SaveChangesAsync(cancellationToken);

        // Signs out every other device. The caller's own session survives only if they sent a
        // refresh token that is theirs and still active; otherwise everything is revoked.
        var keepTokenHash = await GetOwnActiveTokenHashAsync(userId, request.RefreshToken, cancellationToken);
        if (keepTokenHash is null)
            await refreshTokenRepository.RevokeAllForUserAsync(userId, cancellationToken);
        else
            await refreshTokenRepository.RevokeAllForUserExceptAsync(userId, keepTokenHash, cancellationToken);

        // The password is already changed at this point: an SMTP outage must not turn that into
        // an error response (the failure is logged by the sender itself).
        try
        {
            await emailSender.SendAsync(user.Email, "Sua senha foi alterada - Sunsetsss", $"""
                <p>Olá, {user.Name}!</p>
                <p>A senha da sua conta no Sunsetsss foi alterada e os outros dispositivos foram desconectados.</p>
                <p>Se não foi você, redefina sua senha imediatamente pela opção "Esqueci minha senha" na tela de login.</p>
                """, cancellationToken);
        }
        catch (ExternalServiceException)
        {
        }
    }

    private async Task<string?> GetOwnActiveTokenHashAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var tokenHash = TokenHasher.Hash(refreshToken);
        var stored = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        return stored is { IsActive: true } && stored.UserId == userId ? tokenHash : null;
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

using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class ModerationService(
    IReportRepository reportRepository,
    IModerationActionRepository moderationActionRepository,
    ILegalDocumentRepository legalDocumentRepository,
    IUserRepository userRepository,
    IPhotoRepository photoRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IEmailSender emailSender,
    IBanStatusService banStatusService,
    ILocationRepository locationRepository,
    IPasswordHasher passwordHasher) : IModerationService
{
    public async Task<ReportResponse> CreateReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CreateReportRequest request, CancellationToken cancellationToken = default)
    {
        var targetExists = targetType == ReportTargetType.Photo
            ? await photoRepository.GetByIdAsync(targetId, cancellationToken) is not null
            : await photoRepository.GetCommentByIdAsync(targetId, cancellationToken) is not null;

        if (!targetExists)
            throw new NotFoundException($"{targetType} not found.");

        if (await reportRepository.ExistsAsync(reporterId, targetType, targetId, cancellationToken))
            throw new ConflictException("You have already reported this.");

        var report = new Report(reporterId, targetType, targetId, request.Reason, request.Details);
        await reportRepository.AddAsync(report, cancellationToken);

        var created = await reportRepository.GetByIdAsync(report.Id, cancellationToken)
            ?? throw new NotFoundException("Report not found.");

        return created.ToResponse();
    }

    public async Task<CursorPagedResult<ReportResponse>> GetReportsAsync(ReportStatus status, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var page = await reportRepository.GetByStatusAsync(status, cursor, limit, cancellationToken);
        var items = page.Items.Select(r => r.ToResponse()).ToList();

        return new CursorPagedResult<ReportResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<ReportResponse> ResolveReportAsync(Guid moderatorId, Guid reportId, ResolveReportRequest request, CancellationToken cancellationToken = default)
    {
        var report = await reportRepository.GetByIdAsync(reportId, cancellationToken)
            ?? throw new NotFoundException("Report not found.");

        report.Resolve(moderatorId, request.Status);
        await reportRepository.SaveChangesAsync(cancellationToken);

        var actionType = request.Status == ReportStatus.Resolved ? ModerationActionType.ReportResolved : ModerationActionType.ReportDismissed;
        await moderationActionRepository.AddAsync(
            new ModerationAction(moderatorId, actionType, $"Report:{reportId}"),
            cancellationToken);

        return report.ToResponse();
    }

    public async Task<LegalDocumentResponse> GetCurrentLegalDocumentAsync(LegalDocumentType documentType, CancellationToken cancellationToken = default)
    {
        var document = await legalDocumentRepository.GetCurrentAsync(documentType, cancellationToken)
            ?? throw new NotFoundException($"{documentType} has not been published yet.");

        return document.ToResponse();
    }

    public async Task<LegalDocumentResponse> UpdateLegalDocumentAsync(Guid adminId, LegalDocumentType documentType, UpdateLegalDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var current = await legalDocumentRepository.GetCurrentAsync(documentType, cancellationToken);
        var nextVersion = (current?.Version ?? 0) + 1;

        var document = new LegalDocument(documentType, request.Content, nextVersion, adminId);
        await legalDocumentRepository.AddAsync(document, cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(adminId, ModerationActionType.LegalDocumentUpdated, $"{documentType}:v{nextVersion}"),
            cancellationToken);

        return document.ToResponse();
    }

    public async Task<UserResponse> ChangeUserRoleAsync(Guid adminId, Guid targetUserId, ChangeUserRoleRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        user.ChangeRole(request.Role);
        await userRepository.SaveChangesAsync(cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(adminId, ModerationActionType.UserRoleChanged, $"User:{targetUserId}", $"New role: {request.Role}"),
            cancellationToken);

        return user.ToResponse();
    }

    public async Task DeleteLocationAsync(Guid moderatorId, Guid locationId, CancellationToken cancellationToken = default)
    {
        // The query filter already hides an already-deleted location, so this is a 404 for it too.
        var location = await locationRepository.GetByIdAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        // Truncated to microseconds (Postgres precision): the photos are stamped with this exact
        // value and restore matches on equality, so it must survive a round trip through the DB.
        var now = DateTime.UtcNow;
        var deletedAt = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);

        location.SoftDelete(moderatorId, deletedAt);
        await locationRepository.SaveChangesAsync(cancellationToken);
        var photosHidden = await photoRepository.SoftDeleteAllByLocationAsync(locationId, moderatorId, deletedAt, cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(moderatorId, ModerationActionType.LocationDeleted, $"Location:{locationId}",
                $"{location.Name} ({location.City}); {photosHidden} photo(s) hidden"),
            cancellationToken);
    }

    public async Task RestoreLocationAsync(Guid moderatorId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdIncludingDeletedAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");
        if (location.DeletedAt is not { } deletedAt)
            throw new ConflictException("Location is not deleted.");

        location.Restore();
        await locationRepository.SaveChangesAsync(cancellationToken);
        var photosRestored = await photoRepository.RestoreAllByLocationAsync(locationId, deletedAt, cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(moderatorId, ModerationActionType.LocationRestored, $"Location:{locationId}",
                $"{location.Name} ({location.City}); {photosRestored} photo(s) restored"),
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeletedLocationResponse>> GetDeletedLocationsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var locations = await locationRepository.GetDeletedAsync(limit, cancellationToken);
        return locations
            .Select(l => new DeletedLocationResponse(l.Id, l.Name, l.City, l.Latitude, l.Longitude, l.DeletedAt!.Value, l.DeletedByUserId))
            .ToList();
    }

    public async Task<LocationResponse> UpdateLocationCoordinatesAsync(Guid moderatorId, Guid locationId, UpdateLocationCoordinatesRequest request, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        var (oldLat, oldLng) = (location.Latitude, location.Longitude);
        location.ChangeCoordinates(request.Latitude, request.Longitude);
        await locationRepository.SaveChangesAsync(cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(moderatorId, ModerationActionType.LocationCoordinatesChanged, $"Location:{locationId}",
                FormattableString.Invariant($"({oldLat}, {oldLng}) -> ({request.Latitude}, {request.Longitude})")),
            cancellationToken);

        return location.ToResponse();
    }

    public async Task AnonymizeUserAsync(Guid adminId, Guid targetUserId, AnonymizeUserRequest request, CancellationToken cancellationToken = default)
    {
        var target = await GetModeratableUserAsync(adminId, targetUserId, cancellationToken);
        if (target.Email.EndsWith("@sunset.invalid", StringComparison.Ordinal))
            throw new ConflictException("User is already deleted.");

        var reason = request.Reason.Trim();
        var originalEmail = target.Email;
        var originalName = target.Name;

        var photosHidden = 0;
        var commentsHidden = 0;
        if (request.HideContent)
        {
            // Comments first: hiding them needs their photos still visible (to fix the counters),
            // and hiding the photos right after is a single bulk update.
            commentsHidden = await HideCommentsAsync(targetUserId, adminId, cancellationToken);
            var now = DateTime.UtcNow;
            photosHidden = await photoRepository.SoftDeleteAllByUserAsync(
                targetUserId, adminId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc), cancellationToken);
        }

        // Same erasure as the user deleting their own account (UserService.DeleteAccountAsync),
        // plus a permanent ban so an access token they already hold dies at once instead of
        // lingering up to 15 minutes (IBanStatusService is checked on every request).
        target.Anonymize($"deleted-{targetUserId:N}@sunset.invalid", passwordHasher.Hash(Guid.NewGuid().ToString()));
        target.Ban("Account removed by moderation.", null, adminId, DateTime.UtcNow);
        await userRepository.SaveChangesAsync(cancellationToken);
        await refreshTokenRepository.RevokeAllForUserAsync(targetUserId, cancellationToken);
        banStatusService.Invalidate(targetUserId);

        await moderationActionRepository.AddAsync(
            new ModerationAction(adminId, ModerationActionType.UserAnonymized, $"User:{targetUserId}",
                request.HideContent
                    ? $"Reason: {reason}; hidden {photosHidden} photo(s) and {commentsHidden} comment(s)"
                    : $"Reason: {reason}; content kept"),
            cancellationToken);

        // Sent to the address as it was before erasure; a failed send doesn't undo the removal.
        try
        {
            await emailSender.SendAsync(originalEmail, "Sua conta foi removida - Sunsetsss", $"""
                <p>Olá, {System.Net.WebUtility.HtmlEncode(originalName)}!</p>
                <p>Sua conta no Sunsetsss foi removida pela equipe de moderação.</p>
                <p>Motivo: {System.Net.WebUtility.HtmlEncode(reason)}</p>
                """, cancellationToken);
        }
        catch (ExternalServiceException)
        {
        }
    }

    // Mirrors PhotoService.DeleteCommentAsync (counters of the photo and of the parent comment,
    // replies of a hidden root hidden with it), over every comment of one user.
    private async Task<int> HideCommentsAsync(Guid userId, Guid moderatorId, CancellationToken cancellationToken)
    {
        var hidden = 0;
        var comments = await photoRepository.GetCommentsByUserAsync(userId, cancellationToken);

        // Roots first, so a user's reply under their own root is hidden by the cascade (and
        // counted once) rather than twice.
        foreach (var comment in comments.OrderBy(c => c.ParentCommentId is not null))
        {
            if (comment.DeletedAt is not null)
                continue;

            var photo = await photoRepository.GetByIdAsync(comment.PhotoId, cancellationToken);

            if (comment.ParentCommentId is { } parentId)
            {
                var parent = await photoRepository.GetCommentByIdAsync(parentId, cancellationToken);
                parent?.DecrementRepliesCount();
                photo?.DecrementCommentsCount();
                comment.SoftDelete(moderatorId);
                await photoRepository.SoftDeleteCommentAsync(comment, cancellationToken);
                hidden++;
            }
            else
            {
                var replies = await photoRepository.GetAllRepliesAsync(comment.Id, cancellationToken);
                photo?.DecrementCommentsCount(1 + replies.Count);
                comment.SoftDelete(moderatorId);
                await photoRepository.SoftDeleteCommentAsync(comment, cancellationToken);
                hidden++;

                foreach (var reply in replies)
                {
                    reply.SoftDelete(moderatorId);
                    await photoRepository.SoftDeleteCommentAsync(reply, cancellationToken);
                    hidden++;
                }
            }
        }

        return hidden;
    }

    public async Task BanUserAsync(Guid moderatorId, Guid targetUserId, BanUserRequest request, CancellationToken cancellationToken = default)
    {
        var target = await GetModeratableUserAsync(moderatorId, targetUserId, cancellationToken);

        var reason = request.Reason.Trim();
        var until = request.Until?.ToUniversalTime();
        target.Ban(reason, until, moderatorId, DateTime.UtcNow);
        await userRepository.SaveChangesAsync(cancellationToken);

        // Signs the user out everywhere; the access token already issued stops working
        // immediately through IBanStatusService (see Program.cs, OnTokenValidated).
        await refreshTokenRepository.RevokeAllForUserAsync(targetUserId, cancellationToken);
        banStatusService.Invalidate(targetUserId);

        await moderationActionRepository.AddAsync(
            new ModerationAction(moderatorId, ModerationActionType.UserBanned, $"User:{targetUserId}",
                until is null ? $"Permanent. Reason: {reason}" : $"Until {until:O}. Reason: {reason}"),
            cancellationToken);

        // The ban is already in force: an SMTP/API outage must not turn that into an error.
        try
        {
            var duration = until is null ? "permanentemente" : $"até {until:dd/MM/yyyy HH:mm} (UTC)";
            await emailSender.SendAsync(target.Email, "Sua conta foi suspensa - Sunsetsss", $"""
                <p>Olá, {System.Net.WebUtility.HtmlEncode(target.Name)}!</p>
                <p>Sua conta no Sunsetsss foi suspensa {duration}.</p>
                <p>Motivo: {System.Net.WebUtility.HtmlEncode(reason)}</p>
                <p>Se você acredita que isso foi um engano, fale com a equipe do Sunsetsss.</p>
                """, cancellationToken);
        }
        catch (ExternalServiceException)
        {
        }
    }

    public async Task UnbanUserAsync(Guid moderatorId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        var target = await GetModeratableUserAsync(moderatorId, targetUserId, cancellationToken);

        target.Unban();
        await userRepository.SaveChangesAsync(cancellationToken);
        banStatusService.Invalidate(targetUserId);

        await moderationActionRepository.AddAsync(
            new ModerationAction(moderatorId, ModerationActionType.UserUnbanned, $"User:{targetUserId}"),
            cancellationToken);
    }

    // Who may be banned/unbanned/removed: nobody bans themselves, an Admin is untouchable, and a
    // Moderator is only reachable by an Admin (peers can't act on each other).
    private async Task<User> GetModeratableUserAsync(Guid moderatorId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var moderator = await userRepository.GetByIdAsync(moderatorId, cancellationToken)
            ?? throw new UnauthorizedActionException("User is not authenticated.");
        var target = await userRepository.GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (target.Id == moderator.Id)
            throw new ForbiddenException("You cannot act on yourself.");
        if (target.Role == UserRole.Admin)
            throw new ForbiddenException("Admins cannot be banned or removed.");
        if (target.Role == UserRole.Moderator && moderator.Role != UserRole.Admin)
            throw new ForbiddenException("Only an Admin can act on a Moderator.");

        return target;
    }

    public async Task<CursorPagedResult<UserResponse>> SearchUsersAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var page = await userRepository.SearchAsync(query, cursor, limit, cancellationToken);
        var items = page.Items.Select(u => u.ToResponse()).ToList();

        return new CursorPagedResult<UserResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<CursorPagedResult<ModerationActionResponse>> GetActionsAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var page = await moderationActionRepository.GetAllAsync(cursor, limit, cancellationToken);
        var items = page.Items.Select(a => a.ToResponse()).ToList();

        return new CursorPagedResult<ModerationActionResponse>(items, page.NextCursor, page.HasMore);
    }
}

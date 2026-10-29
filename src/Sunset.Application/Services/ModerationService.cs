using Sunset.Application.Common;
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
    IBanStatusService banStatusService) : IModerationService
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

    // Who may be banned/unbanned: nobody bans themselves, an Admin is untouchable, and a
    // Moderator is only reachable by an Admin (peers can't act on each other).
    private async Task<User> GetModeratableUserAsync(Guid moderatorId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var moderator = await userRepository.GetByIdAsync(moderatorId, cancellationToken)
            ?? throw new UnauthorizedActionException("User is not authenticated.");
        var target = await userRepository.GetByIdAsync(targetUserId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (target.Id == moderator.Id)
            throw new ForbiddenException("You cannot ban yourself.");
        if (target.Role == UserRole.Admin)
            throw new ForbiddenException("Admins cannot be banned.");
        if (target.Role == UserRole.Moderator && moderator.Role != UserRole.Admin)
            throw new ForbiddenException("Only an Admin can ban a Moderator.");

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

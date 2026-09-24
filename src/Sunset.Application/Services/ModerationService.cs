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
    IPhotoRepository photoRepository) : IModerationService
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
}

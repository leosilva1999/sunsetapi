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
    ITermsOfServiceRepository termsOfServiceRepository,
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

    public async Task<TermsOfServiceResponse> GetCurrentTermsAsync(CancellationToken cancellationToken = default)
    {
        var terms = await termsOfServiceRepository.GetCurrentAsync(cancellationToken)
            ?? throw new NotFoundException("Terms of service have not been published yet.");

        return terms.ToResponse();
    }

    public async Task<TermsOfServiceResponse> UpdateTermsAsync(Guid adminId, UpdateTermsOfServiceRequest request, CancellationToken cancellationToken = default)
    {
        var current = await termsOfServiceRepository.GetCurrentAsync(cancellationToken);
        var nextVersion = (current?.Version ?? 0) + 1;

        var terms = new TermsOfService(request.Content, nextVersion, adminId);
        await termsOfServiceRepository.AddAsync(terms, cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(adminId, ModerationActionType.TermsOfServiceUpdated, $"TermsOfService:v{nextVersion}"),
            cancellationToken);

        return terms.ToResponse();
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

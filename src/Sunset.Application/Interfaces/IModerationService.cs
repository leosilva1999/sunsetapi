using Sunset.Application.Common;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Users;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface IModerationService
{
    Task<ReportResponse> CreateReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CreateReportRequest request, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<ReportResponse>> GetReportsAsync(ReportStatus status, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<ReportResponse> ResolveReportAsync(Guid moderatorId, Guid reportId, ResolveReportRequest request, CancellationToken cancellationToken = default);

    Task<TermsOfServiceResponse> GetCurrentTermsAsync(CancellationToken cancellationToken = default);
    Task<TermsOfServiceResponse> UpdateTermsAsync(Guid adminId, UpdateTermsOfServiceRequest request, CancellationToken cancellationToken = default);

    Task<UserResponse> ChangeUserRoleAsync(Guid adminId, Guid targetUserId, ChangeUserRoleRequest request, CancellationToken cancellationToken = default);
}

using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Users;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface IModerationService
{
    Task<ReportResponse> CreateReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CreateReportRequest request, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<ReportResponse>> GetReportsAsync(ReportStatus status, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<ReportResponse> ResolveReportAsync(Guid moderatorId, Guid reportId, ResolveReportRequest request, CancellationToken cancellationToken = default);

    Task<LegalDocumentResponse> GetCurrentLegalDocumentAsync(LegalDocumentType documentType, CancellationToken cancellationToken = default);
    Task<LegalDocumentResponse> UpdateLegalDocumentAsync(Guid adminId, LegalDocumentType documentType, UpdateLegalDocumentRequest request, CancellationToken cancellationToken = default);

    Task<UserResponse> ChangeUserRoleAsync(Guid adminId, Guid targetUserId, ChangeUserRoleRequest request, CancellationToken cancellationToken = default);
    Task DeleteLocationAsync(Guid moderatorId, Guid locationId, CancellationToken cancellationToken = default);
    Task RestoreLocationAsync(Guid moderatorId, Guid locationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeletedLocationResponse>> GetDeletedLocationsAsync(int limit, CancellationToken cancellationToken = default);
    Task<LocationResponse> MergeLocationsAsync(Guid moderatorId, Guid sourceLocationId, MergeLocationsRequest request, CancellationToken cancellationToken = default);
    Task<LocationResponse> UpdateLocationCoordinatesAsync(Guid moderatorId, Guid locationId, UpdateLocationCoordinatesRequest request, CancellationToken cancellationToken = default);

    Task AnonymizeUserAsync(Guid adminId, Guid targetUserId, AnonymizeUserRequest request, CancellationToken cancellationToken = default);
    Task BanUserAsync(Guid moderatorId, Guid targetUserId, BanUserRequest request, CancellationToken cancellationToken = default);
    Task UnbanUserAsync(Guid moderatorId, Guid targetUserId, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<UserResponse>> SearchUsersAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken = default);

    Task<CursorPagedResult<ModerationActionResponse>> GetActionsAsync(string? cursor, int limit, CancellationToken cancellationToken = default);
}

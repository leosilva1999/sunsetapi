using Sunset.Domain.Entities;

namespace Sunset.Application.DTOs.Moderation;

public static class ReportExtensions
{
    public static ReportResponse ToResponse(this Report report) =>
        new(
            report.Id,
            report.ReporterId,
            report.Reporter.Name,
            report.TargetType,
            report.TargetId,
            report.Reason,
            report.Details,
            report.Status,
            report.CreatedAt,
            report.ResolvedByUserId,
            report.ResolvedAt);
}

using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

public class Report : BaseEntity
{
    public Guid ReporterId { get; private set; }
    public ReportTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public ReportReason Reason { get; private set; }
    public string? Details { get; private set; }
    public ReportStatus Status { get; private set; } = ReportStatus.Pending;
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public User Reporter { get; private set; } = null!;

    private Report() { }

    public Report(Guid reporterId, ReportTargetType targetType, Guid targetId, ReportReason reason, string? details = null)
    {
        if (reporterId == Guid.Empty)
            throw new ArgumentException("ReporterId is required.", nameof(reporterId));
        if (targetId == Guid.Empty)
            throw new ArgumentException("TargetId is required.", nameof(targetId));

        ReporterId = reporterId;
        TargetType = targetType;
        TargetId = targetId;
        Reason = reason;
        Details = details;
    }

    public void Resolve(Guid resolvedByUserId, ReportStatus finalStatus)
    {
        if (finalStatus == ReportStatus.Pending)
            throw new ArgumentException("Final status must be Resolved or Dismissed.", nameof(finalStatus));

        Status = finalStatus;
        ResolvedByUserId = resolvedByUserId;
        ResolvedAt = DateTime.UtcNow;
    }
}

using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Announcements;

/// <param name="Type">All, Users or PushEnabled.</param>
/// <param name="UserIds">Only for Users: 1 to 20 account ids.</param>
public sealed record AnnouncementAudienceRequest(AnnouncementAudience Type, List<Guid>? UserIds = null);

public sealed record PreviewAnnouncementRequest(AnnouncementCategory Category, AnnouncementAudienceRequest Audience);

/// <param name="ExpectedRecipients">The number the preview gave. If the real audience differs now, nothing is sent (409).</param>
public sealed record CreateAnnouncementRequest(
    string Title,
    string Body,
    AnnouncementCategory Category,
    AnnouncementAudienceRequest Audience,
    int ExpectedRecipients,
    string? Link = null);

public sealed record AnnouncementPreviewResponse(int Recipients);

/// <param name="ExpectedRecipients">What the sender was told to expect.</param>
/// <param name="CreatedCount">Notifications created so far (it can end below the expectation: people who left, or reached a daily cap, in between).</param>
/// <param name="ReadCount">How many of them have been read.</param>
public sealed record AnnouncementResponse(
    Guid Id,
    string Title,
    string Body,
    AnnouncementCategory Category,
    AnnouncementAudience Audience,
    IReadOnlyList<Guid> AudienceUserIds,
    string? Link,
    AnnouncementStatus Status,
    int ExpectedRecipients,
    int CreatedCount,
    int ReadCount,
    Guid SentByUserId,
    DateTime CreatedAt,
    DateTime? CompletedAt);

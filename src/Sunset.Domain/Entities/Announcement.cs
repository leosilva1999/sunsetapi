using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

/// <summary>
/// A message from the team to the people of the app (service notices, promotions). The announcement is the content;
/// each recipient gets a <see cref="Notification"/> pointing at it, so it shows in their list, counts as unread and can
/// be pushed to the phone like any other notification.
/// </summary>
public class Announcement : BaseEntity
{
    public const int TitleMaxLength = 65;
    public const int BodyMaxLength = 240;
    public const int LinkMaxLength = 200;
    public const int MaxUserAudience = 20;

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public AnnouncementCategory Category { get; private set; }
    public AnnouncementAudience Audience { get; private set; }

    /// <summary>Only for <see cref="AnnouncementAudience.Users"/>.</summary>
    public List<Guid> AudienceUserIds { get; private set; } = [];

    /// <summary>Where tapping it leads inside the app: "Location:{id}", "City:{slug}", "Photo:{id}" or "User:{id}". Never an external URL.</summary>
    public string? Link { get; private set; }

    /// <summary>The staff account that sent it. Never shown to the recipients.</summary>
    public Guid CreatedByUserId { get; private set; }

    public AnnouncementStatus Status { get; private set; } = AnnouncementStatus.Pending;

    /// <summary>How many people it was going to reach when it was sent (what the preview promised).</summary>
    public int ExpectedRecipients { get; private set; }

    /// <summary>Notifications created so far.</summary>
    public int CreatedCount { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    private Announcement() { }

    public Announcement(
        Guid createdByUserId, string title, string body, AnnouncementCategory category, AnnouncementAudience audience,
        IEnumerable<Guid>? audienceUserIds, string? link, int expectedRecipients)
    {
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > TitleMaxLength)
            throw new ArgumentException($"Title is required and at most {TitleMaxLength} characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > BodyMaxLength)
            throw new ArgumentException($"Body is required and at most {BodyMaxLength} characters.", nameof(body));
        if (expectedRecipients < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRecipients));

        var ids = (audienceUserIds ?? []).Distinct().ToList();
        if (audience == AnnouncementAudience.Users && (ids.Count == 0 || ids.Count > MaxUserAudience))
            throw new ArgumentException($"A user audience lists between 1 and {MaxUserAudience} accounts.", nameof(audienceUserIds));
        if (audience != AnnouncementAudience.Users && ids.Count > 0)
            throw new ArgumentException("Only a user audience lists accounts.", nameof(audienceUserIds));

        CreatedByUserId = createdByUserId;
        Title = title.Trim();
        Body = body.Trim();
        Category = category;
        Audience = audience;
        AudienceUserIds = ids;
        Link = string.IsNullOrWhiteSpace(link) ? null : link.Trim();
        ExpectedRecipients = expectedRecipients;
    }

    public void MarkSending()
    {
        if (Status == AnnouncementStatus.Pending)
            Status = AnnouncementStatus.Sending;
    }

    public void AddCreated(int count) => CreatedCount += count;

    public void Complete(DateTime utcNow)
    {
        Status = AnnouncementStatus.Sent;
        CompletedAt = utcNow;
    }
}

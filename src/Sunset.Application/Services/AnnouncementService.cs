using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Announcements;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public partial class AnnouncementService(
    IAnnouncementRepository announcementRepository,
    IModerationActionRepository moderationActionRepository) : IAnnouncementService
{
    /// <summary>More than this many announcements to the whole base in a day is refused: a mistake or a compromised account cannot spam everyone.</summary>
    public const int MaxMassAnnouncementsPerDay = 5;

    // Where the app may be sent: its own screens only, never an external address.
    [GeneratedRegex(@"^(Location|City|Photo|User):[A-Za-z0-9-]{1,100}$")]
    private static partial Regex LinkRegex();

    public async Task<AnnouncementPreviewResponse> PreviewAsync(bool isAdmin, PreviewAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        var spec = ToSpec(isAdmin, request.Audience, request.Category);
        return new AnnouncementPreviewResponse(await announcementRepository.CountRecipientsAsync(spec, cancellationToken));
    }

    public async Task<AnnouncementResponse> CreateAsync(Guid senderId, bool isAdmin, CreateAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        if (title.Length is 0 or > Announcement.TitleMaxLength)
            throw Invalid("title", $"'title' is required and at most {Announcement.TitleMaxLength} characters.");
        if (body.Length is 0 or > Announcement.BodyMaxLength)
            throw Invalid("body", $"'body' is required and at most {Announcement.BodyMaxLength} characters.");
        if (!Enum.IsDefined(request.Category))
            throw Invalid("category", "Unknown category.");
        if (!string.IsNullOrWhiteSpace(request.Link) && (request.Link.Length > Announcement.LinkMaxLength || !LinkRegex().IsMatch(request.Link.Trim())))
            throw Invalid("link", "'link' must be a destination inside the app: Location:{id}, City:{slug}, Photo:{id} or User:{id}.");
        if (request.ExpectedRecipients < 0)
            throw Invalid("expectedRecipients", "'expectedRecipients' must be the number the preview returned.");

        var spec = ToSpec(isAdmin, request.Audience, request.Category);

        if (spec.Type != AnnouncementAudience.Users
            && await announcementRepository.CountMassSinceAsync(DateTime.UtcNow.AddHours(-24), cancellationToken) >= MaxMassAnnouncementsPerDay)
            throw new ConflictException($"The limit of {MaxMassAnnouncementsPerDay} announcements to everyone per day was reached. Try again tomorrow.");

        // The sender confirms the audience they saw: if it changed meanwhile (or the preview was for something else), nothing goes out.
        var recipients = await announcementRepository.CountRecipientsAsync(spec, cancellationToken);
        if (recipients == 0)
            throw Invalid("audience", "Nobody would receive this announcement.");
        if (recipients != request.ExpectedRecipients)
            throw new ConflictException(
                $"The audience is now {recipients} people, not the {request.ExpectedRecipients} you confirmed. Preview it again.",
                new Dictionary<string, string[]> { ["recipients"] = [recipients.ToString()] });

        var announcement = new Announcement(senderId, title, body, request.Category, spec.Type, spec.UserIds, request.Link, recipients);
        await announcementRepository.AddAsync(announcement, cancellationToken);

        await moderationActionRepository.AddAsync(
            new ModerationAction(senderId, ModerationActionType.AnnouncementSent, $"Announcement:{announcement.Id}",
                $"{spec.Type}; {request.Category}; {recipients} recipient(s); \"{title}\""),
            cancellationToken);

        return ToResponse(announcement, 0);
    }

    public async Task<CursorPagedResult<AnnouncementResponse>> ListAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var page = await announcementRepository.ListAsync(cursor, limit, cancellationToken);
        var reads = await announcementRepository.GetReadCountsAsync(page.Items.Select(a => a.Id).ToList(), cancellationToken);

        return new CursorPagedResult<AnnouncementResponse>(
            page.Items.Select(a => ToResponse(a, reads.GetValueOrDefault(a.Id))).ToList(), page.NextCursor, page.HasMore);
    }

    public async Task<AnnouncementResponse> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var announcement = await announcementRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Announcement not found.");
        var reads = await announcementRepository.GetReadCountsAsync([id], cancellationToken);

        return ToResponse(announcement, reads.GetValueOrDefault(id));
    }

    private static AnnouncementAudienceSpec ToSpec(bool isAdmin, AnnouncementAudienceRequest audience, AnnouncementCategory category)
    {
        if (audience is null || !Enum.IsDefined(audience.Type))
            throw Invalid("audience", "Unknown audience. Use All, Users or PushEnabled.");
        if (!Enum.IsDefined(category))
            throw Invalid("category", "Unknown category.");

        var ids = (audience.UserIds ?? []).Distinct().ToList();
        if (audience.Type == AnnouncementAudience.Users)
        {
            if (ids.Count is 0 or > Announcement.MaxUserAudience || ids.Contains(Guid.Empty))
                throw Invalid("audience", $"A list of accounts holds between 1 and {Announcement.MaxUserAudience} valid ids.");
        }
        else
        {
            if (ids.Count > 0)
                throw Invalid("audience", "Only the Users audience lists accounts.");
            if (!isAdmin)
                throw new ForbiddenException("Only an administrator can send an announcement to everyone; a moderator can write to up to 20 named accounts.");
        }

        return new AnnouncementAudienceSpec(audience.Type, category, ids);
    }

    private static AnnouncementResponse ToResponse(Announcement a, int readCount) =>
        new(a.Id, a.Title, a.Body, a.Category, a.Audience, a.AudienceUserIds, a.Link, a.Status, a.ExpectedRecipients, a.CreatedCount,
            readCount, a.CreatedByUserId, a.CreatedAt, a.CompletedAt);

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}

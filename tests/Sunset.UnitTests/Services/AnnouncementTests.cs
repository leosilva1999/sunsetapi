using FluentValidation;
using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Announcements;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class AnnouncementEntityTests
{
    private static Announcement Make(string title = "Manutenção", string body = "O app ficará fora do ar por 10 minutos.",
        AnnouncementAudience audience = AnnouncementAudience.All, IEnumerable<Guid>? users = null, string? link = null) =>
        new(Guid.NewGuid(), title, body, AnnouncementCategory.Service, audience, users, link, 10);

    [Fact]
    public void TheTextIsTrimmed_AndStartsPending()
    {
        var announcement = Make("  Novidade  ", "  Olá!  ");

        Assert.Equal(("Novidade", "Olá!", AnnouncementStatus.Pending), (announcement.Title, announcement.Body, announcement.Status));
    }

    [Theory]
    [InlineData("", "corpo")]
    [InlineData("   ", "corpo")]
    [InlineData("título", "")]
    public void ATitleAndABodyAreRequired(string title, string body) =>
        Assert.Throws<ArgumentException>(() => Make(title, body));

    [Fact]
    public void TheLengthsAreCapped()
    {
        Make(new string('a', Announcement.TitleMaxLength), new string('b', Announcement.BodyMaxLength));

        Assert.Throws<ArgumentException>(() => Make(new string('a', Announcement.TitleMaxLength + 1)));
        Assert.Throws<ArgumentException>(() => Make(body: new string('b', Announcement.BodyMaxLength + 1)));
    }

    [Fact]
    public void AUserAudienceNeedsBetweenOneAndTwentyAccounts_AndNoOtherAudienceMayListAny()
    {
        Assert.Throws<ArgumentException>(() => Make(audience: AnnouncementAudience.Users));
        Assert.Throws<ArgumentException>(() => Make(audience: AnnouncementAudience.Users, users: Enumerable.Range(0, 21).Select(_ => Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => Make(audience: AnnouncementAudience.All, users: [Guid.NewGuid()]));
        var ok = Make(audience: AnnouncementAudience.Users, users: Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()));
        Assert.Equal(20, ok.AudienceUserIds.Count);
    }

    [Fact]
    public void RepeatedAccountsCountOnce()
    {
        var id = Guid.NewGuid();

        Assert.Single(Make(audience: AnnouncementAudience.Users, users: [id, id, id]).AudienceUserIds);
    }

    [Fact]
    public void ItMovesFromPendingToSendingToSent_CountingWhatWasCreated()
    {
        var announcement = Make();

        announcement.MarkSending();
        announcement.AddCreated(200);
        announcement.AddCreated(37);
        announcement.Complete(new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal((AnnouncementStatus.Sent, 237), (announcement.Status, announcement.CreatedCount));
        Assert.NotNull(announcement.CompletedAt);
    }

    [Fact]
    public void ANotificationForAnAnnouncement_PointsAtIt_AndCarriesTheStaffSenderAsItsActor()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var notification = Notification.ForAnnouncement(recipient, sender, announcementId);

        Assert.Equal((NotificationType.Announcement, recipient, sender, announcementId), (notification.Type, notification.RecipientUserId, notification.ActorUserId, notification.AnnouncementId));
        Assert.Equal($"Announcement:{announcementId}", notification.TargetDescription);
    }

    [Fact]
    public void PromotionalConsent_IsOffByDefault_AndReplacedWithTheRest()
    {
        var setting = new NotificationSetting(Guid.NewGuid());
        Assert.False(setting.PromotionalEnabled);

        setting.Replace(true, [], promotionalEnabled: true);
        Assert.True(setting.PromotionalEnabled);

        setting.Replace(true, []);   // an older client that does not know the flag turns it off again: the safe direction
        Assert.False(setting.PromotionalEnabled);
    }
}

public class AnnouncementResponseTests
{
    [Fact]
    public void AnAnnouncementInTheList_ShowsTheTeamsContent_AndHidesWhoSentIt()
    {
        var staff = new User("Moderadora Maria", "maria@sunset.com", "hashed", "https://sunset/maria.png");
        var announcement = new Announcement(staff.Id, "Promoção", "Veja os locais da semana", AnnouncementCategory.Promotional, AnnouncementAudience.All, null, "City:imbituba-sc", 5);
        var notification = Notification.ForAnnouncement(Guid.NewGuid(), staff.Id, announcement.Id);
        typeof(Notification).GetProperty(nameof(Notification.Announcement))!.SetValue(notification, announcement);
        typeof(Notification).GetProperty(nameof(Notification.Actor))!.SetValue(notification, staff);

        var response = notification.ToResponse();

        Assert.Equal(("Promoção", "Veja os locais da semana", AnnouncementCategory.Promotional, "City:imbituba-sc", announcement.Id),
            (response.Title, response.Body, response.Category, response.Link, response.AnnouncementId));
        Assert.Equal("Equipe Sunset", response.ActorName);
        Assert.Null(response.ActorAvatarUrl);
        Assert.Equal(Guid.Empty, response.ActorUserId);
        Assert.DoesNotContain("Maria", response.ActorName);
    }

    [Fact]
    public void AnOrdinaryNotification_IsUnchanged()
    {
        var actor = new User("Ana", "ana@sunset.com", "hashed");
        var notification = new Notification(Guid.NewGuid(), NotificationType.PhotoLiked, actor.Id, "Photo:1");
        typeof(Notification).GetProperty(nameof(Notification.Actor))!.SetValue(notification, actor);

        var response = notification.ToResponse();

        Assert.Equal(("Ana", actor.Id), (response.ActorName, response.ActorUserId));
        Assert.Null(response.Title);
        Assert.Null(response.AnnouncementId);
    }
}

public class AnnouncementServiceTests
{
    private readonly Mock<IAnnouncementRepository> _repository = new();
    private readonly Mock<IModerationActionRepository> _audit = new();
    private readonly AnnouncementService _sut;
    private readonly Guid _sender = Guid.NewGuid();
    private readonly Guid _someone = Guid.NewGuid();

    public AnnouncementServiceTests()
    {
        _repository.Setup(r => r.CountRecipientsAsync(It.IsAny<AnnouncementAudienceSpec>(), default)).ReturnsAsync(42);
        _repository.Setup(r => r.CountMassSinceAsync(It.IsAny<DateTime>(), default)).ReturnsAsync(0);
        _sut = new AnnouncementService(_repository.Object, _audit.Object);
    }

    private static AnnouncementAudienceRequest Everyone => new(AnnouncementAudience.All);
    private AnnouncementAudienceRequest Named => new(AnnouncementAudience.Users, [_someone]);

    private CreateAnnouncementRequest Request(AnnouncementAudienceRequest? audience = null, int expected = 42, string? link = null, string title = "Aviso", string body = "Texto do aviso.",
        AnnouncementCategory category = AnnouncementCategory.Service) =>
        new(title, body, category, audience ?? Everyone, expected, link);

    [Fact]
    public async Task AnAdminSendsToEveryone_ItIsSavedPending_AndAudited()
    {
        var response = await _sut.CreateAsync(_sender, true, Request());

        Assert.Equal((AnnouncementStatus.Pending, 42, AnnouncementAudience.All), (response.Status, response.ExpectedRecipients, response.Audience));
        _repository.Verify(r => r.AddAsync(It.Is<Announcement>(a => a.CreatedByUserId == _sender && a.ExpectedRecipients == 42), default), Times.Once);
        _audit.Verify(a => a.AddAsync(It.Is<ModerationAction>(m =>
            m.ActionType == ModerationActionType.AnnouncementSent && m.ModeratorId == _sender && m.Notes!.Contains("42 recipient")), default), Times.Once);
    }

    [Fact]
    public async Task AModerator_MayWriteToNamedAccounts_ButNotToEveryoneOrToPushUsers()
    {
        await _sut.CreateAsync(_sender, false, Request(Named));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateAsync(_sender, false, Request(Everyone)));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateAsync(_sender, false, Request(new AnnouncementAudienceRequest(AnnouncementAudience.PushEnabled))));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.PreviewAsync(false, new PreviewAnnouncementRequest(AnnouncementCategory.Service, Everyone)));
    }

    [Fact]
    public async Task TheConfirmedCountMustMatch_OtherwiseNothingIsSavedAndTheRealNumberIsReported()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(_sender, true, Request(expected: 40)));

        Assert.Equal(["42"], ex.Errors!["recipients"]);
        _repository.Verify(r => r.AddAsync(It.IsAny<Announcement>(), default), Times.Never);
    }

    [Fact]
    public async Task NobodyToReceiveIt_IsAValidationError()
    {
        _repository.Setup(r => r.CountRecipientsAsync(It.IsAny<AnnouncementAudienceSpec>(), default)).ReturnsAsync(0);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_sender, true, Request(expected: 0)));
    }

    [Fact]
    public async Task OnlyFiveAnnouncementsToEveryonePerDay_ButNamedAccountsAreNotCounted()
    {
        _repository.Setup(r => r.CountMassSinceAsync(It.IsAny<DateTime>(), default)).ReturnsAsync(AnnouncementService.MaxMassAnnouncementsPerDay);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(_sender, true, Request()));
        await _sut.CreateAsync(_sender, true, Request(Named));   // still fine
    }

    [Theory]
    [InlineData("City:imbituba-sc", true)]
    [InlineData("Location:4f0c7a22-6d7e-4e6f-9a0e-3f0d6a0b9c11", true)]
    [InlineData("Photo:abc-123", true)]
    [InlineData("User:abc", true)]
    [InlineData("https://evil.example/login", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("Settings:privacy", false)]
    [InlineData("City:", false)]
    [InlineData("City:a b", false)]
    public async Task OnlyDestinationsInsideTheAppAreAcceptedAsALink(string link, bool accepted)
    {
        var send = () => _sut.CreateAsync(_sender, true, Request(Named, link: link));

        if (accepted)
            Assert.Equal(link, (await send()).Link);
        else
            await Assert.ThrowsAsync<ValidationException>(send);
    }

    [Theory]
    [InlineData("", "corpo")]
    [InlineData("título", "")]
    public async Task ATitleAndABodyAreRequired(string title, string body) =>
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_sender, true, Request(Named, title: title, body: body)));

    [Fact]
    public async Task TheTextHasLimits()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_sender, true, Request(Named, title: new string('t', Announcement.TitleMaxLength + 1))));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_sender, true, Request(Named, body: new string('b', Announcement.BodyMaxLength + 1))));
    }

    [Fact]
    public async Task AUserAudienceIsCheckedBeforeAnythingIsCounted()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.PreviewAsync(true, new PreviewAnnouncementRequest(AnnouncementCategory.Service, new AnnouncementAudienceRequest(AnnouncementAudience.Users, []))));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.PreviewAsync(true, new PreviewAnnouncementRequest(AnnouncementCategory.Service,
            new AnnouncementAudienceRequest(AnnouncementAudience.Users, Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToList()))));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.PreviewAsync(true, new PreviewAnnouncementRequest(AnnouncementCategory.Service,
            new AnnouncementAudienceRequest(AnnouncementAudience.All, [Guid.NewGuid()]))));
        _repository.Verify(r => r.CountRecipientsAsync(It.IsAny<AnnouncementAudienceSpec>(), default), Times.Never);
    }

    [Fact]
    public async Task ThePreviewCountsUsingTheCategory()
    {
        AnnouncementAudienceSpec? asked = null;
        _repository.Setup(r => r.CountRecipientsAsync(It.IsAny<AnnouncementAudienceSpec>(), default))
            .Callback((AnnouncementAudienceSpec s, CancellationToken _) => asked = s).ReturnsAsync(7);

        var preview = await _sut.PreviewAsync(true, new PreviewAnnouncementRequest(AnnouncementCategory.Promotional, Everyone));

        Assert.Equal(7, preview.Recipients);
        Assert.Equal((AnnouncementAudience.All, AnnouncementCategory.Promotional), (asked!.Type, asked.Category));
    }

    [Fact]
    public async Task TheHistoryCarriesTheReadCounts_AndAnUnknownIdIs404()
    {
        var announcement = new Announcement(_sender, "A", "B", AnnouncementCategory.Service, AnnouncementAudience.All, null, null, 3);
        _repository.Setup(r => r.ListAsync(null, 10, default)).ReturnsAsync(new CursorPagedResult<Announcement>([announcement], null, false));
        _repository.Setup(r => r.GetReadCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, int> { [announcement.Id] = 2 });
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Announcement?)null);

        var page = await _sut.ListAsync(null, 10);

        Assert.Equal(2, Assert.Single(page.Items).ReadCount);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Guid.NewGuid()));
    }
}

public class AnnouncementDeliveryServiceTests
{
    private readonly Mock<IAnnouncementRepository> _repository = new();
    private readonly Mock<IPushDispatchService> _push = new();
    private readonly PushSettings _settings = new() { AnnouncementBatchSize = 2 };
    private readonly AnnouncementDeliveryService _sut;
    private readonly Announcement _announcement = new(Guid.NewGuid(), "Aviso", "Texto", AnnouncementCategory.Service, AnnouncementAudience.All, null, null, 5);

    public AnnouncementDeliveryServiceTests() => _sut = new AnnouncementDeliveryService(_repository.Object, _push.Object, _settings);

    [Fact]
    public async Task WithNothingToDeliver_ItDoesNothing()
    {
        _repository.Setup(r => r.GetNextToDeliverAsync(default)).ReturnsAsync((Announcement?)null);

        Assert.False(await _sut.DeliverNextAsync());
    }

    [Fact]
    public async Task ItWorksInBatches_PushesEach_AndMarksItSentWhenTheLastBatchIsEmpty()
    {
        _repository.Setup(r => r.GetNextToDeliverAsync(default)).ReturnsAsync(_announcement);
        var batches = new Queue<IReadOnlyList<Guid>>([
            [Guid.NewGuid(), Guid.NewGuid()], [Guid.NewGuid(), Guid.NewGuid()], [Guid.NewGuid()], []]);
        _repository.Setup(r => r.GetNextRecipientIdsAsync(_announcement, 2, default)).ReturnsAsync(() => batches.Dequeue());
        _repository.Setup(r => r.CreateNotificationsAsync(_announcement, It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync((Announcement a, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
            {
                a.AddCreated(ids.Count);
                return (IReadOnlyList<(Guid, Guid)>)ids.Select(id => (id, Guid.NewGuid())).ToList();
            });

        var worked = await _sut.DeliverNextAsync();

        Assert.True(worked);
        Assert.Equal((AnnouncementStatus.Sent, 5), (_announcement.Status, _announcement.CreatedCount));
        Assert.NotNull(_announcement.CompletedAt);
        _push.Verify(p => p.DispatchAnnouncementAsync(_announcement, It.IsAny<IReadOnlyCollection<(Guid UserId, Guid NotificationId)>>(), default), Times.Exactly(3));
    }

    [Fact]
    public async Task WhenCancelledMidWay_ItStopsWithoutMarkingItSent_SoTheNextRunResumes()
    {
        using var cts = new CancellationTokenSource();
        _repository.Setup(r => r.GetNextToDeliverAsync(cts.Token)).ReturnsAsync(_announcement);
        _repository.Setup(r => r.GetNextRecipientIdsAsync(_announcement, 2, cts.Token)).ReturnsAsync([Guid.NewGuid()]);
        _repository.Setup(r => r.CreateNotificationsAsync(_announcement, It.IsAny<IReadOnlyCollection<Guid>>(), cts.Token))
            .Callback(() => cts.Cancel()).ReturnsAsync([]);

        await _sut.DeliverNextAsync(cts.Token);

        Assert.Equal(AnnouncementStatus.Sending, _announcement.Status);
        Assert.Null(_announcement.CompletedAt);
    }
}

public class AnnouncementPushTests
{
    private readonly Mock<IPushDeviceRepository> _devices = new();
    private readonly Mock<IPushTicketRepository> _tickets = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<INotificationSettingRepository> _choices = new();
    private readonly Mock<IPushSender> _sender = new();
    private readonly PushSettings _settings = new() { Enabled = true, GroupWindowSeconds = 0 };
    private readonly PushDispatchService _sut;
    private readonly Announcement _announcement = new(Guid.NewGuid(), "Promoção da semana", "Veja os locais mais bonitos.", AnnouncementCategory.Promotional, AnnouncementAudience.All, null, "City:imbituba-sc", 3);
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _bia = Guid.NewGuid();
    private readonly Dictionary<Guid, NotificationSetting> _saved = [];

    public AnnouncementPushTests()
    {
        _devices.Setup(r => r.ListByUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync([
            new PushDevice(_ana, "ExponentPushToken[ana-phone]", PushPlatform.Android, null, null, null),
            new PushDevice(_ana, "ExponentPushToken[ana-tablet]", PushPlatform.Ios, null, null, null),
            new PushDevice(_bia, "ExponentPushToken[bia-phone]", PushPlatform.Android, null, null, null)]);
        _notifications.Setup(r => r.GetUnreadCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, int> { [_ana] = 3 });
        _choices.Setup(c => c.GetByUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(() => _saved);
        _sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default))
            .ReturnsAsync((IReadOnlyList<PushMessage> m, CancellationToken _) => (IReadOnlyList<PushSendResult>)m.Select((_, i) => PushSendResult.Accepted($"t{i}")).ToList());
        _sut = new PushDispatchService(_devices.Object, _tickets.Object, _users.Object, _notifications.Object, _choices.Object, _sender.Object,
            new PushThrottle(_settings), new PushGrouper(_settings), _settings);
    }

    [Fact]
    public async Task EveryDeviceGetsTheTeamsOwnTextAndTheAppDestination()
    {
        var notificationAna = Guid.NewGuid();
        var notificationBia = Guid.NewGuid();

        await _sut.DispatchAnnouncementAsync(_announcement, [(_ana, notificationAna), (_bia, notificationBia)]);

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m =>
            m.Count == 3 && m.All(x => x.Title == "Promoção da semana" && x.Body == "Veja os locais mais bonitos.") &&
            m.Where(x => x.To.Contains("ana")).All(x => x.Badge == 3 && (Guid)x.Data["notificationId"] == notificationAna) &&
            m.Single(x => x.To.Contains("bia")).Badge == 0 &&
            m.All(x => (string)x.Data["type"] == "Announcement" && (string)x.Data["category"] == "Promotional" && (string)x.Data["link"] == "City:imbituba-sc" &&
                       (Guid)x.Data["announcementId"] == _announcement.Id)), default), Times.Once);
        _tickets.Verify(t => t.AddRangeAsync(It.Is<IReadOnlyCollection<PushTicket>>(x => x.Count == 3), default), Times.Once);
    }

    [Fact]
    public async Task APersonWhoSwitchedPushesOffGetsNoPush_TheOtherStillDoes()
    {
        var off = new NotificationSetting(_ana);
        off.Replace(false, [], promotionalEnabled: true);
        _saved[_ana] = off;

        await _sut.DispatchAnnouncementAsync(_announcement, [(_ana, Guid.NewGuid()), (_bia, Guid.NewGuid())]);

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 1 && m[0].To == "ExponentPushToken[bia-phone]"), default), Times.Once);
    }

    [Fact]
    public async Task ItIsNotHeldByGroupingNorCountedAgainstTheHourlyLimit()
    {
        _settings.GroupWindowSeconds = 60;
        _settings.MaxPushesPerUserPerHour = 0;   // would silence every social push

        await _sut.DispatchAnnouncementAsync(_announcement, [(_ana, Guid.NewGuid())]);

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 2), default), Times.Once);
    }

    [Fact]
    public async Task WithPushSwitchedOffOnTheServer_NothingIsSent()
    {
        _settings.Enabled = false;

        await _sut.DispatchAnnouncementAsync(_announcement, [(_ana, Guid.NewGuid())]);

        _sender.Verify(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default), Times.Never);
    }

    [Fact]
    public async Task ALinkIsLeftOutOfTheDataWhenThereIsNone()
    {
        var plain = new Announcement(Guid.NewGuid(), "Aviso", "Texto", AnnouncementCategory.Service, AnnouncementAudience.All, null, null, 1);

        var message = PushAnnouncementComposer.Compose("ExponentPushToken[x]", plain, Guid.NewGuid(), 0);

        Assert.DoesNotContain("link", message.Data.Keys);
    }
}

public class NotificationSettingsPromotionalTests
{
    private readonly Mock<INotificationSettingRepository> _repository = new();
    private readonly NotificationSettingsService _sut;
    private NotificationSetting? _stored;
    private readonly Guid _user = Guid.NewGuid();

    public NotificationSettingsPromotionalTests()
    {
        _repository.Setup(r => r.GetByUserAsync(_user, default)).ReturnsAsync(() => _stored);
        _repository.Setup(r => r.AddAsync(It.IsAny<NotificationSetting>(), default)).Callback((NotificationSetting s, CancellationToken _) => _stored = s).Returns(Task.CompletedTask);
        _sut = new NotificationSettingsService(_repository.Object);
    }

    [Fact]
    public async Task PromotionsStartOff_AndTheKindsListHasNoAnnouncementEntry()
    {
        var result = await _sut.GetAsync(_user);

        Assert.False(result.PromotionalEnabled);
        Assert.DoesNotContain("Announcement", result.Types.Keys);
    }

    [Fact]
    public async Task TheConsentIsSavedAndReplaced()
    {
        var on = await _sut.UpdateAsync(_user, new UpdateNotificationSettingsRequest(true, null, PromotionalEnabled: true));
        Assert.True(on.PromotionalEnabled);

        var replaced = await _sut.UpdateAsync(_user, new UpdateNotificationSettingsRequest(true));
        Assert.False(replaced.PromotionalEnabled);   // the flag was not sent: replaced by "off"
    }

    [Fact]
    public async Task AnnouncementIsNotAKindThatCanBeMuted() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAsync(_user, new UpdateNotificationSettingsRequest(true, new() { ["Announcement"] = false })));
}

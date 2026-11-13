using FluentValidation;
using Moq;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class NotificationSettingEntityTests
{
    [Fact]
    public void ByDefaultEverythingIsOn()
    {
        var setting = new NotificationSetting(Guid.NewGuid());

        Assert.All(Enum.GetValues<NotificationType>(), type => Assert.True(setting.AllowsPush(type)));
    }

    [Fact]
    public void MutingOneKind_LeavesTheOthersOn_AndTheMasterSwitchSilencesAll()
    {
        var setting = new NotificationSetting(Guid.NewGuid());

        setting.Replace(true, [NotificationType.PhotoLiked]);
        Assert.False(setting.AllowsPush(NotificationType.PhotoLiked));
        Assert.True(setting.AllowsPush(NotificationType.NewFollower));
        Assert.True(setting.AllowsPush(NotificationType.CommentReplied));

        setting.Replace(false, []);
        Assert.All(Enum.GetValues<NotificationType>(), type => Assert.False(setting.AllowsPush(type)));
        Assert.False(setting.IsMuted(NotificationType.PhotoLiked));   // the per-kind flags were replaced: nothing is muted by kind
    }
}

public class NotificationSettingsServiceTests
{
    private readonly Mock<INotificationSettingRepository> _repository = new();
    private readonly NotificationSettingsService _sut;
    private NotificationSetting? _stored;
    private readonly Guid _user = Guid.NewGuid();

    public NotificationSettingsServiceTests()
    {
        _repository.Setup(r => r.GetByUserAsync(_user, default)).ReturnsAsync(() => _stored);
        _repository.Setup(r => r.AddAsync(It.IsAny<NotificationSetting>(), default)).Callback((NotificationSetting s, CancellationToken _) => _stored = s).Returns(Task.CompletedTask);
        _sut = new NotificationSettingsService(_repository.Object);
    }

    [Fact]
    public async Task WithoutSavedChoices_EverythingIsOn_AndAllFourKindsAreListed()
    {
        var result = await _sut.GetAsync(_user);

        Assert.True(result.PushEnabled);
        Assert.Equal(["NewFollower", "PhotoLiked", "PhotoCommented", "CommentReplied"], result.Types.Keys);
        Assert.All(result.Types.Values, Assert.True);
    }

    [Fact]
    public async Task Saving_ReplacesTheChoices_AKindLeftOutIsOn_AndLaterSavesUpdateTheSameRow()
    {
        var first = await _sut.UpdateAsync(_user, new UpdateNotificationSettingsRequest(true, new() { ["photoliked"] = false, ["NewFollower"] = true }));
        Assert.False(first.Types["PhotoLiked"]);
        Assert.True(first.Types["PhotoCommented"]);          // not mentioned: on

        var second = await _sut.UpdateAsync(_user, new UpdateNotificationSettingsRequest(false, null));
        Assert.False(second.PushEnabled);
        Assert.True(second.Types["PhotoLiked"]);             // replaced: the old mute is gone

        _repository.Verify(r => r.AddAsync(It.IsAny<NotificationSetting>(), default), Times.Once);   // one row, updated in place
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task AnUnknownKind_IsAValidationError_AndNothingIsSaved()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAsync(_user, new UpdateNotificationSettingsRequest(true, new() { ["PhotoShared"] = false })));

        _repository.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }
}

public class PushGroupingTests
{
    private readonly PushSettings _settings = new() { Enabled = true, GroupWindowSeconds = 60 };
    private readonly PushGrouper _grouper;
    private readonly Guid _owner = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

    public PushGroupingTests() => _grouper = new PushGrouper(_settings);

    private PushRequest Like(Guid actor, string photo = "Photo:1", Guid? recipient = null) =>
        new(Guid.NewGuid(), recipient ?? _owner, actor, NotificationType.PhotoLiked, photo);

    [Fact]
    public void TheFirstGoesAtOnce_TheRestWaitForTheWindowAndLeaveTogether()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(_grouper.OfferSendNow(Like(a), T0));                       // goes now, window opens
        Assert.False(_grouper.OfferSendNow(Like(b), T0.AddSeconds(5)));
        Assert.False(_grouper.OfferSendNow(Like(c), T0.AddSeconds(20)));
        Assert.False(_grouper.OfferSendNow(Like(d), T0.AddSeconds(40)));

        Assert.Empty(_grouper.TakeDue(T0.AddSeconds(59)));                     // window still open
        var group = Assert.Single(_grouper.TakeDue(T0.AddSeconds(61)));
        Assert.Equal((b, 2), (group.FirstActorId, group.OtherActors));         // "Bia e mais 2 curtiram"
        Assert.Equal(d, group.Latest.ActorUserId);                             // the push opens the latest notification
    }

    [Fact]
    public void AfterAGroupLeaves_AFreshWindowOpens_AndAQuietOneIsForgotten()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _grouper.OfferSendNow(Like(a), T0);
        _grouper.OfferSendNow(Like(b), T0.AddSeconds(10));
        Assert.Single(_grouper.TakeDue(T0.AddSeconds(61)));

        Assert.False(_grouper.OfferSendNow(Like(c), T0.AddSeconds(70)));       // inside the window that opened at the flush
        Assert.Single(_grouper.TakeDue(T0.AddSeconds(125)));
        Assert.Empty(_grouper.TakeDue(T0.AddSeconds(200)));                    // nothing held: dropped
        Assert.True(_grouper.OfferSendNow(Like(Guid.NewGuid()), T0.AddSeconds(300)));   // a new burst starts with an immediate push
    }

    [Fact]
    public void DifferentPhotos_RecipientsAndKinds_AreSeparateGroups()
    {
        Assert.True(_grouper.OfferSendNow(Like(Guid.NewGuid(), "Photo:1"), T0));
        Assert.True(_grouper.OfferSendNow(Like(Guid.NewGuid(), "Photo:2"), T0));                          // another photo
        Assert.True(_grouper.OfferSendNow(Like(Guid.NewGuid(), "Photo:1", Guid.NewGuid()), T0));          // another person
        Assert.True(_grouper.OfferSendNow(new PushRequest(Guid.NewGuid(), _owner, Guid.NewGuid(), NotificationType.PhotoCommented, "Photo:1"), T0));   // another kind
    }

    [Fact]
    public void AllNewFollowersOfAPersonAreOneGroup_EvenThoughEachTargetsItsFollower()
    {
        PushRequest Follow() { var f = Guid.NewGuid(); return new(Guid.NewGuid(), _owner, f, NotificationType.NewFollower, $"User:{f}"); }

        Assert.True(_grouper.OfferSendNow(Follow(), T0));
        Assert.False(_grouper.OfferSendNow(Follow(), T0.AddSeconds(5)));
        Assert.False(_grouper.OfferSendNow(Follow(), T0.AddSeconds(6)));

        Assert.Equal(1, Assert.Single(_grouper.TakeDue(T0.AddSeconds(61))).OtherActors);   // two held: the first of them plus one more
    }

    [Fact]
    public void ThePeopleCountedAreDistinct_SoOneChattyCommenterIsNotACrowd()
    {
        var chatty = Guid.NewGuid();
        PushRequest Comment(Guid actor) => new(Guid.NewGuid(), _owner, actor, NotificationType.PhotoCommented, "Photo:1");

        _grouper.OfferSendNow(Comment(Guid.NewGuid()), T0);
        _grouper.OfferSendNow(Comment(chatty), T0.AddSeconds(5));
        _grouper.OfferSendNow(Comment(chatty), T0.AddSeconds(6));
        _grouper.OfferSendNow(Comment(chatty), T0.AddSeconds(7));

        var group = Assert.Single(_grouper.TakeDue(T0.AddSeconds(61)));
        Assert.Equal(0, group.OtherActors);   // three comments, one person: it reads as a single comment, not "and 2 more"
    }

    [Fact]
    public void WithAZeroWindow_NothingIsEverHeld()
    {
        var off = new PushGrouper(new PushSettings { GroupWindowSeconds = 0 });

        Assert.True(off.OfferSendNow(Like(Guid.NewGuid()), T0));
        Assert.True(off.OfferSendNow(Like(Guid.NewGuid()), T0));
    }

    [Theory]
    [InlineData(NotificationType.NewFollower, "Novos seguidores", "Ana e mais 3 começaram a seguir você")]
    [InlineData(NotificationType.PhotoLiked, "Novas curtidas", "Ana e mais 3 curtiram sua foto")]
    [InlineData(NotificationType.PhotoCommented, "Novos comentários", "Ana e mais 3 comentaram na sua foto")]
    [InlineData(NotificationType.CommentReplied, "Novas respostas", "Ana e mais 3 responderam seu comentário")]
    public void AGroupedPushNamesTheFirstPersonAndCountsTheOthers(NotificationType type, string title, string body)
    {
        var request = new PushRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, "Photo:1");

        var message = PushMessageComposer.Compose("ExponentPushToken[x]", request, "Ana", unreadCount: 7, otherActors: 3);

        Assert.Equal((title, body, 7), (message.Title, message.Body, message.Badge));
        Assert.Equal(4, message.Data["count"]);          // Ana + 3 more
        Assert.Equal(request.NotificationId, message.Data["notificationId"]);
    }

    [Fact]
    public void ASinglePushHasNoCount()
    {
        var request = new PushRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.PhotoLiked, "Photo:1");

        Assert.DoesNotContain("count", PushMessageComposer.Compose("ExponentPushToken[x]", request, "Ana", 1).Data.Keys);
    }
}

public class PushPhase2DispatchTests
{
    private readonly Mock<IPushDeviceRepository> _devices = new();
    private readonly Mock<IPushTicketRepository> _tickets = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<INotificationSettingRepository> _choices = new();
    private readonly Mock<IPushSender> _sender = new();
    private readonly PushSettings _settings = new() { Enabled = true, GroupWindowSeconds = 60 };
    private readonly PushDispatchService _sut;
    private readonly Dictionary<Guid, NotificationSetting> _saved = [];

    private readonly User _owner = new("Dono", "dono@sunset.com", "hashed");
    private readonly User _ana = new("Ana", "ana@sunset.com", "hashed");
    private readonly User _bia = new("Bia", "bia@sunset.com", "hashed");
    private readonly User _caio = new("Caio", "caio@sunset.com", "hashed");

    public PushPhase2DispatchTests()
    {
        foreach (var user in new[] { _owner, _ana, _bia, _caio })
            _users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _devices.Setup(r => r.ListByUserAsync(_owner.Id, default))
            .ReturnsAsync([new PushDevice(_owner.Id, "ExponentPushToken[phone]", PushPlatform.Android, null, null, null)]);
        _notifications.Setup(r => r.GetUnreadCountAsync(It.IsAny<Guid>(), default)).ReturnsAsync(5);
        _choices.Setup(c => c.GetByUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(() => _saved);
        _sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default))
            .ReturnsAsync((IReadOnlyList<PushMessage> m, CancellationToken _) => (IReadOnlyList<PushSendResult>)m.Select((_, i) => PushSendResult.Accepted($"t{i}")).ToList());
        _sut = new PushDispatchService(_devices.Object, _tickets.Object, _users.Object, _notifications.Object, _choices.Object, _sender.Object,
            new PushThrottle(_settings), new PushGrouper(_settings), _settings);
    }

    private PushRequest Like(User actor) => new(Guid.NewGuid(), _owner.Id, actor.Id, NotificationType.PhotoLiked, "Photo:1");

    [Fact]
    public async Task ABurst_SendsOnePushNowAndOneGroupedPushWhenTheWindowEnds()
    {
        _settings.GroupWindowSeconds = 1;

        await _sut.DispatchAsync([Like(_ana), Like(_bia), Like(_caio)]);

        // now: only the first, in the normal wording
        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 1 && m[0].Body == "Ana curtiu sua foto"), default), Times.Once);
        _sender.Invocations.Clear();

        // when the window ends: the other two leave as one push, naming the first of them
        await Task.Delay(1300);
        await _sut.FlushDueGroupsAsync();
        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m =>
            m.Count == 1 && m[0].Title == "Novas curtidas" && m[0].Body == "Bia e mais 1 curtiram sua foto" && m[0].Badge == 5), default), Times.Once);

        // and nothing is left over
        _sender.Invocations.Clear();
        await _sut.FlushDueGroupsAsync();
        _sender.Verify(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default), Times.Never);
    }

    [Fact]
    public async Task APersonWhoTurnedPushOff_OrOneKindOff_GetsNoPush_AndOthersAreUnaffected()
    {
        var offAll = new NotificationSetting(_owner.Id);
        offAll.Replace(false, []);
        _saved[_owner.Id] = offAll;
        await _sut.DispatchAsync([Like(_ana)]);

        var offLikes = new NotificationSetting(_owner.Id);
        offLikes.Replace(true, [NotificationType.PhotoLiked]);
        _saved[_owner.Id] = offLikes;
        await _sut.DispatchAsync([Like(_bia)]);
        await _sut.DispatchAsync([new PushRequest(Guid.NewGuid(), _owner.Id, _caio.Id, NotificationType.NewFollower, $"User:{_caio.Id}")]);   // another kind: still on

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 1 && m[0].Title == "Novo seguidor"), default), Times.Once);
        _sender.Verify(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default), Times.Once);
    }

    [Fact]
    public async Task AMutedKindOpensNoGroupWindow()
    {
        var offLikes = new NotificationSetting(_owner.Id);
        offLikes.Replace(true, [NotificationType.PhotoLiked]);
        _saved[_owner.Id] = offLikes;
        await _sut.DispatchAsync([Like(_ana), Like(_bia)]);

        _saved.Clear();   // turned back on: the next like is the first of a burst, so it goes at once
        await _sut.DispatchAsync([Like(_caio)]);

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 1 && m[0].Body == "Caio curtiu sua foto"), default), Times.Once);
    }

    // ---------------------------------------------------------------- the test push

    [Fact]
    public async Task TheTestPush_GoesToEveryDeviceAtOnce_AndBooksTheTicketsAndDeadTokens()
    {
        _devices.Setup(r => r.ListByUserAsync(_owner.Id, default)).ReturnsAsync(
        [
            new PushDevice(_owner.Id, "ExponentPushToken[alive]", PushPlatform.Android, null, null, null),
            new PushDevice(_owner.Id, "ExponentPushToken[dead]", PushPlatform.Ios, null, null, null),
        ]);
        _devices.Setup(r => r.DeleteByTokenAsync("ExponentPushToken[dead]", default)).ReturnsAsync(1);
        _sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default)).ReturnsAsync(
            (IReadOnlyList<PushSendResult>)[PushSendResult.Accepted("t1"), PushSendResult.Failed(PushFailure.DeviceNotRegistered)]);

        var result = await _sut.SendTestAsync(_owner.Id);

        Assert.Equal(new PushTestResult(2, 1, 1), result);
        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.All(x => x.Title == "Teste de notificação" && x.Badge == 5)), default), Times.Once);
        _tickets.Verify(t => t.AddRangeAsync(It.Is<IReadOnlyCollection<PushTicket>>(x => x.Count == 1), default), Times.Once);
    }

    [Fact]
    public async Task TheTestPush_ForSomeoneWithoutDevices_ReachesNobody_AndIsConflictWhilePushIsOff()
    {
        _devices.Setup(r => r.ListByUserAsync(_ana.Id, default)).ReturnsAsync([]);
        Assert.Equal(new PushTestResult(0, 0, 0), await _sut.SendTestAsync(_ana.Id));

        _settings.Enabled = false;
        await Assert.ThrowsAsync<ConflictException>(() => _sut.SendTestAsync(_owner.Id));
    }
}

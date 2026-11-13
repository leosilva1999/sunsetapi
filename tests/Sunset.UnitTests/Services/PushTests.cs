using Moq;
using Sunset.Application.DTOs.Devices;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Push;
using Sunset.Application.Services;
using Sunset.Application.Validators.Devices;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class PushHelpersTests
{
    [Theory]
    [InlineData("ExponentPushToken[abcDEF123_-xyz]", true)]
    [InlineData("ExpoPushToken[abc]", true)]
    [InlineData("ExponentPushToken[]", false)]
    [InlineData("fcm:APA91bE...", false)]
    [InlineData("ExponentPushToken[abc", false)]
    [InlineData("", false)]
    public void OnlyExpoTokensAreAccepted(string token, bool valid) =>
        Assert.Equal(valid, PushTokens.IsValid(token));

    [Fact]
    public void ATokenTooLong_IsRefused() =>
        Assert.False(PushTokens.IsValid("ExponentPushToken[" + new string('a', 300) + "]"));

    [Fact]
    public void MaskedTokens_NeverShowMoreThanTheirLastCharacters()
    {
        var masked = PushTokens.Mask("ExponentPushToken[abcdefghijklmnop]");

        Assert.DoesNotContain("ExponentPushToken", masked);
        Assert.DoesNotContain("abcdefgh", masked);
        Assert.Equal("…klmnop", masked);
        Assert.Equal("(none)", PushTokens.Mask(null));
    }

    [Theory]
    [InlineData(NotificationType.NewFollower, "Novo seguidor", "Ana começou a seguir você")]
    [InlineData(NotificationType.PhotoLiked, "Nova curtida", "Ana curtiu sua foto")]
    [InlineData(NotificationType.PhotoCommented, "Novo comentário", "Ana comentou na sua foto")]
    [InlineData(NotificationType.CommentReplied, "Nova resposta", "Ana respondeu seu comentário")]
    public void TheTextIsComposedInPortuguese_ForEachKind(NotificationType type, string title, string body)
    {
        var request = new PushRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, "Photo:1");

        var message = PushMessageComposer.Compose("ExponentPushToken[x]", request, "Ana", unreadCount: 3);

        Assert.Equal((title, body, 3, "ExponentPushToken[x]"), (message.Title, message.Body, message.Badge, message.To));
    }

    [Fact]
    public void TheDataCarriesOnlyIdsAndLabels_WithAVersion()
    {
        var request = new PushRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.PhotoLiked, "Photo:abc");

        var data = PushMessageComposer.Compose("ExponentPushToken[x]", request, "Ana", 1).Data;

        Assert.Equal(["v", "notificationId", "type", "actorUserId", "targetDescription"], data.Keys);
        Assert.Equal(1, data["v"]);
        Assert.Equal("PhotoLiked", data["type"]);
        Assert.Equal(request.NotificationId, data["notificationId"]);
        Assert.Equal("Photo:abc", data["targetDescription"]);
    }

    [Fact]
    public void TheThrottle_AllowsTheLimitPerHourPerPerson_AndSlidesTheWindow()
    {
        var throttle = new PushThrottle(new PushSettings { MaxPushesPerUserPerHour = 3 });
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(throttle.TryAcquire(ana, t0));
        Assert.True(throttle.TryAcquire(ana, t0.AddMinutes(10)));
        Assert.True(throttle.TryAcquire(ana, t0.AddMinutes(20)));
        Assert.False(throttle.TryAcquire(ana, t0.AddMinutes(30)));      // the 4th within the hour
        Assert.True(throttle.TryAcquire(bia, t0.AddMinutes(30)));       // someone else is unaffected
        Assert.True(throttle.TryAcquire(ana, t0.AddMinutes(61)));       // the first one left the window
        Assert.False(throttle.TryAcquire(ana, t0.AddMinutes(62)));
    }

    [Fact]
    public void TrimmingForgetsPeopleWhoseWindowIsEmpty()
    {
        var throttle = new PushThrottle(new PushSettings { MaxPushesPerUserPerHour = 1 });
        var ana = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        throttle.TryAcquire(ana, t0);

        throttle.Trim(t0.AddHours(2));

        Assert.True(throttle.TryAcquire(ana, t0.AddHours(2)));
    }
}

public class DeviceRequestValidatorTests
{
    [Fact]
    public void ARegistrationNeedsAnExpoTokenAndAKnownPlatform()
    {
        var validator = new RegisterDeviceRequestValidator();

        Assert.True(validator.Validate(new RegisterDeviceRequest("ExponentPushToken[abc]", PushPlatform.Android)).IsValid);
        Assert.False(validator.Validate(new RegisterDeviceRequest("nope", PushPlatform.Android)).IsValid);
        Assert.False(validator.Validate(new RegisterDeviceRequest("ExponentPushToken[abc]", (PushPlatform)9)).IsValid);
        Assert.False(validator.Validate(new RegisterDeviceRequest("ExponentPushToken[abc]", PushPlatform.Ios, Guid.Empty)).IsValid);
        Assert.False(validator.Validate(new RegisterDeviceRequest("ExponentPushToken[abc]", PushPlatform.Ios, null, new string('9', 33))).IsValid);
    }

    [Fact]
    public void ARemovalNeedsATokenOrAnInstallId()
    {
        var validator = new RemoveDeviceRequestValidator();

        Assert.False(validator.Validate(new RemoveDeviceRequest()).IsValid);
        Assert.True(validator.Validate(new RemoveDeviceRequest(Token: "ExponentPushToken[abc]")).IsValid);
        Assert.True(validator.Validate(new RemoveDeviceRequest(InstallId: Guid.NewGuid())).IsValid);
    }
}

public class PushDeviceServiceTests
{
    private readonly Mock<IPushDeviceRepository> _repository = new();
    private readonly List<PushDevice> _store = [];
    private readonly PushDeviceService _sut;
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _bia = Guid.NewGuid();

    public PushDeviceServiceTests()
    {
        // A tiny in-memory stand-in for the table, so the service's decisions can be followed end to end.
        _repository.Setup(r => r.GetByTokenAsync(It.IsAny<string>(), default)).ReturnsAsync((string t, CancellationToken _) => _store.FirstOrDefault(d => d.Token == t));
        _repository.Setup(r => r.GetByInstallAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid u, Guid i, CancellationToken _) => _store.FirstOrDefault(d => d.UserId == u && d.InstallId == i));
        _repository.Setup(r => r.ListByUserAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid u, CancellationToken _) => (IReadOnlyList<PushDevice>)_store.Where(d => d.UserId == u).OrderByDescending(d => d.LastSeenAt).ToList());
        _repository.Setup(r => r.AddAsync(It.IsAny<PushDevice>(), default)).Callback((PushDevice d, CancellationToken _) => _store.Add(d)).Returns(Task.CompletedTask);
        _repository.Setup(r => r.Remove(It.IsAny<PushDevice>())).Callback((PushDevice d) => _store.Remove(d));
        _sut = new PushDeviceService(_repository.Object, new PushSettings { MaxDevicesPerUser = 5 });
    }

    private static string Token(string n) => $"ExponentPushToken[{n}]";

    [Fact]
    public async Task RegisteringTwice_IsIdempotent_AndKeepsOneRow()
    {
        var request = new RegisterDeviceRequest(Token("a"), PushPlatform.Android, Guid.NewGuid(), "1.0.0", "pt-BR");

        await _sut.RegisterAsync(_ana, request);
        await _sut.RegisterAsync(_ana, request with { AppVersion = "1.0.1" });

        var device = Assert.Single(_store);
        Assert.Equal((_ana, "1.0.1", "pt-BR"), (device.UserId, device.AppVersion, device.Locale));
    }

    [Fact]
    public async Task ATokenHeldByAnotherAccount_MovesToTheCaller()
    {
        await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token("shared"), PushPlatform.Ios));

        await _sut.RegisterAsync(_bia, new RegisterDeviceRequest(Token("shared"), PushPlatform.Ios));

        var device = Assert.Single(_store);
        Assert.Equal(_bia, device.UserId);   // never two owners for one token
    }

    [Fact]
    public async Task AKnownInstallWithANewToken_UpdatesThatRow_InsteadOfAddingOne()
    {
        var install = Guid.NewGuid();
        await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token("old"), PushPlatform.Android, install));

        await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token("rotated"), PushPlatform.Android, install));

        var device = Assert.Single(_store);
        Assert.Equal(Token("rotated"), device.Token);
    }

    [Fact]
    public async Task BeyondFiveDevices_TheLeastRecentlySeenOneIsDropped()
    {
        for (var i = 1; i <= 5; i++)
            await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token($"d{i}"), PushPlatform.Android));
        await Task.Delay(15);   // LastSeenAt has millisecond resolution; make the order unambiguous

        await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token("d6"), PushPlatform.Android));

        Assert.Equal(5, _store.Count);
        Assert.DoesNotContain(_store, d => d.Token == Token("d1"));   // the oldest went
        Assert.Contains(_store, d => d.Token == Token("d6"));
    }

    [Fact]
    public async Task OnlyTheCallersOwnDeviceIsRemoved_AndAnUnknownOneIsAQuietNoOp()
    {
        await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token("mine"), PushPlatform.Android));
        await _sut.RegisterAsync(_bia, new RegisterDeviceRequest(Token("hers"), PushPlatform.Android));

        await _sut.RemoveAsync(_ana, new RemoveDeviceRequest(Token: Token("hers")));      // someone else's: untouched
        await _sut.RemoveAsync(_ana, new RemoveDeviceRequest(Token: Token("unknown")));   // unknown: fine
        Assert.Equal(2, _store.Count);

        await _sut.RemoveAsync(_ana, new RemoveDeviceRequest(Token: Token("mine")));
        Assert.Equal([Token("hers")], _store.Select(d => d.Token));
    }

    [Fact]
    public async Task ADeviceCanBeRemovedByInstallId()
    {
        var install = Guid.NewGuid();
        await _sut.RegisterAsync(_ana, new RegisterDeviceRequest(Token("a"), PushPlatform.Ios, install));

        await _sut.RemoveAsync(_ana, new RemoveDeviceRequest(InstallId: install));

        Assert.Empty(_store);
    }
}

public class PushDispatchServiceTests
{
    private readonly Mock<IPushDeviceRepository> _devices = new();
    private readonly Mock<IPushTicketRepository> _tickets = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IPushSender> _sender = new();
    private readonly Mock<INotificationSettingRepository> _choices = new();
    // Grouping is off here (window 0) so each test sees one push per notification; PushGroupingTests covers the window.
    private readonly PushSettings _settings = new() { Enabled = true, MaxPushesPerUserPerHour = 20, DeviceRetentionDays = 90, GroupWindowSeconds = 0 };
    private readonly PushDispatchService _sut;

    private readonly User _ana = new("Ana", "ana@sunset.com", "hashed");
    private readonly User _bia = new("Bia", "bia@sunset.com", "hashed");

    public PushDispatchServiceTests()
    {
        _users.Setup(r => r.GetByIdAsync(_ana.Id, default)).ReturnsAsync(_ana);
        _users.Setup(r => r.GetByIdAsync(_bia.Id, default)).ReturnsAsync(_bia);
        _notifications.Setup(r => r.GetUnreadCountAsync(It.IsAny<Guid>(), default)).ReturnsAsync(4);
        _sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default))
            .ReturnsAsync((IReadOnlyList<PushMessage> m, CancellationToken _) => (IReadOnlyList<PushSendResult>)m.Select((_, i) => PushSendResult.Accepted($"t{i}")).ToList());
        _choices.Setup(c => c.GetByUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, NotificationSetting>());
        _sut = new PushDispatchService(_devices.Object, _tickets.Object, _users.Object, _notifications.Object, _choices.Object, _sender.Object,
            new PushThrottle(_settings), new PushGrouper(_settings), _settings);
    }

    private void HasDevices(User user, params string[] tokens) =>
        _devices.Setup(r => r.ListByUserAsync(user.Id, default))
            .ReturnsAsync(tokens.Select(t => new PushDevice(user.Id, $"ExponentPushToken[{t}]", PushPlatform.Android, null, null, null)).ToList());

    // Bia likes Ana's photo.
    private PushRequest Like() => new(Guid.NewGuid(), _ana.Id, _bia.Id, NotificationType.PhotoLiked, "Photo:1");

    [Fact]
    public async Task OnePushPerDevice_WithTheActorsNameAndTheUnreadBadge()
    {
        HasDevices(_ana, "phone", "tablet");

        await _sut.DispatchAsync([Like()]);

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m =>
            m.Count == 2 && m.All(x => x.Body == "Bia curtiu sua foto" && x.Badge == 4) &&
            m.Select(x => x.To).SequenceEqual(new[] { "ExponentPushToken[phone]", "ExponentPushToken[tablet]" })), default), Times.Once);
    }

    [Fact]
    public async Task NothingIsSent_WhenPushIsOff()
    {
        _settings.Enabled = false;
        HasDevices(_ana, "phone");

        await _sut.DispatchAsync([Like()]);

        _sender.Verify(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default), Times.Never);
    }

    [Fact]
    public async Task NothingIsSent_ToSomeoneWithoutDevices_Banned_OrRemoved()
    {
        // no devices
        _devices.Setup(r => r.ListByUserAsync(_ana.Id, default)).ReturnsAsync([]);
        await _sut.DispatchAsync([Like()]);

        // banned
        _ana.Ban("spam", null, Guid.NewGuid(), DateTime.UtcNow);
        HasDevices(_ana, "phone");
        await _sut.DispatchAsync([Like()]);

        // removed (anonymized)
        var gone = new User("Excluído", "deleted-x@sunset.invalid", "hashed");
        _users.Setup(r => r.GetByIdAsync(gone.Id, default)).ReturnsAsync(gone);
        _devices.Setup(r => r.ListByUserAsync(gone.Id, default)).ReturnsAsync([new PushDevice(gone.Id, "ExponentPushToken[z]", PushPlatform.Ios, null, null, null)]);
        await _sut.DispatchAsync([new PushRequest(Guid.NewGuid(), gone.Id, _bia.Id, NotificationType.NewFollower, "User:1")]);

        _sender.Verify(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default), Times.Never);
    }

    [Fact]
    public async Task BeyondTheHourlyLimit_TheExcessStaysInAppOnly()
    {
        _settings.MaxPushesPerUserPerHour = 3;
        HasDevices(_ana, "phone");

        await _sut.DispatchAsync(Enumerable.Range(0, 5).Select(_ => Like()).ToList());

        _sender.Verify(s => s.SendAsync(It.Is<IReadOnlyList<PushMessage>>(m => m.Count == 3), default), Times.Once);
    }

    [Fact]
    public async Task AcceptedMessagesLeaveATicket_AndADeadTokenIsRemovedAtOnce()
    {
        HasDevices(_ana, "alive", "dead");
        _sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default)).ReturnsAsync(
            (IReadOnlyList<PushSendResult>)[PushSendResult.Accepted("ticket-1"), PushSendResult.Failed(PushFailure.DeviceNotRegistered)]);

        await _sut.DispatchAsync([Like()]);

        _tickets.Verify(t => t.AddRangeAsync(It.Is<IReadOnlyCollection<PushTicket>>(x =>
            x.Count == 1 && x.Single().ProviderTicketId == "ticket-1" && x.Single().Token == "ExponentPushToken[alive]"), default), Times.Once);
        _devices.Verify(d => d.DeleteByTokenAsync("ExponentPushToken[dead]", default), Times.Once);
        _devices.Verify(d => d.DeleteByTokenAsync("ExponentPushToken[alive]", default), Times.Never);
    }

    [Fact]
    public async Task ATransientFailure_KeepsTheTokenAndWritesNoTicket()
    {
        HasDevices(_ana, "phone");
        _sender.Setup(s => s.SendAsync(It.IsAny<IReadOnlyList<PushMessage>>(), default))
            .ReturnsAsync((IReadOnlyList<PushSendResult>)[PushSendResult.Failed(PushFailure.Transient)]);

        await _sut.DispatchAsync([Like()]);

        _devices.Verify(d => d.DeleteByTokenAsync(It.IsAny<string>(), default), Times.Never);
        _tickets.Verify(t => t.AddRangeAsync(It.IsAny<IReadOnlyCollection<PushTicket>>(), default), Times.Never);
    }

    // ---------------------------------------------------------------- receipts

    [Fact]
    public async Task Receipts_RemoveDeadTokens_AndCloseEveryAnsweredTicket()
    {
        var delivered = new PushTicket("ok", "ExponentPushToken[a]");
        var dead = new PushTicket("dead", "ExponentPushToken[b]");
        var failed = new PushTicket("rejected", "ExponentPushToken[c]");
        _tickets.Setup(t => t.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), default)).ReturnsAsync([delivered, dead, failed]);
        _sender.Setup(s => s.GetReceiptsAsync(It.IsAny<IReadOnlyCollection<string>>(), default)).ReturnsAsync(new Dictionary<string, PushReceipt>
        {
            ["ok"] = new(true, PushFailure.None),
            ["dead"] = new(false, PushFailure.DeviceNotRegistered),
            ["rejected"] = new(false, PushFailure.Rejected),
        });

        await _sut.ProcessReceiptsAsync();

        _devices.Verify(d => d.DeleteByTokenAsync("ExponentPushToken[b]", default), Times.Once);
        _devices.Verify(d => d.DeleteByTokenAsync("ExponentPushToken[a]", default), Times.Never);
        _devices.Verify(d => d.DeleteByTokenAsync("ExponentPushToken[c]", default), Times.Never);   // a rejection says nothing about the token
        _tickets.Verify(t => t.DeleteAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 3), default), Times.Once);
    }

    [Fact]
    public async Task ATicketTheProviderHasNotAnsweredYet_IsKeptUntilItExpires()
    {
        var recent = new PushTicket("recent", "ExponentPushToken[a]");   // created just now
        _tickets.Setup(t => t.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), default)).ReturnsAsync([recent]);
        _sender.Setup(s => s.GetReceiptsAsync(It.IsAny<IReadOnlyCollection<string>>(), default)).ReturnsAsync(new Dictionary<string, PushReceipt>());

        await _sut.ProcessReceiptsAsync();

        _tickets.Verify(t => t.DeleteAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default), Times.Never);
    }

    [Fact]
    public async Task ReceiptsAreNotReadWhilePushIsOff()
    {
        _settings.Enabled = false;

        await _sut.ProcessReceiptsAsync();

        _tickets.Verify(t => t.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task StaleDevices_AreForgottenAfterTheRetentionPeriod_EvenWithPushOff()
    {
        _settings.Enabled = false;
        _devices.Setup(d => d.DeleteNotSeenSinceAsync(It.IsAny<DateTime>(), default)).ReturnsAsync(2);

        var removed = await _sut.DeleteStaleDevicesAsync();

        Assert.Equal(2, removed);
        _devices.Verify(d => d.DeleteNotSeenSinceAsync(
            It.Is<DateTime>(cutoff => Math.Abs((DateTime.UtcNow.AddDays(-90) - cutoff).TotalMinutes) < 1), default), Times.Once);
    }
}

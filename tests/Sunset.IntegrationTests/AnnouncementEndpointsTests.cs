using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Announcements;
using Sunset.Application.DTOs.Devices;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Notifications;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

// Only "named accounts" audiences are used here: an announcement to everyone would notify every user of the shared
// test database, including users of tests that run later.
public class AnnouncementEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Base = "/api/v1/moderation/announcements";

    private static AnnouncementAudienceRequest Named(params Guid[] ids) => new(AnnouncementAudience.Users, ids.ToList());

    private static async Task<int> PreviewAsync(HttpClient staff, AnnouncementCategory category, params Guid[] ids)
    {
        var response = await staff.PostAsJsonAsync($"{Base}/preview", new PreviewAnnouncementRequest(category, Named(ids)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AnnouncementPreviewResponse>())!.Recipients;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient staff, AnnouncementCategory category, int expected, string title, params Guid[] ids) =>
        staff.PostAsJsonAsync(Base, new CreateAnnouncementRequest(title, "Texto do comunicado.", category, Named(ids), expected));

    private static async Task<AnnouncementResponse> WaitUntilSentAsync(HttpClient staff, Guid id)
    {
        for (var i = 0; i < 200; i++)
        {
            var current = (await staff.GetFromJsonAsync<AnnouncementResponse>($"{Base}/{id}"))!;
            if (current.Status == AnnouncementStatus.Sent)
                return current;
            await Task.Delay(100);
        }

        return (await staff.GetFromJsonAsync<AnnouncementResponse>($"{Base}/{id}"))!;
    }

    private static async Task<List<NotificationResponse>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications"))!.Items.ToList();

    // ---------------------------------------------------------------- who may

    [Fact]
    public async Task OnlyStaffCanUseIt()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();
        var body = new PreviewAnnouncementRequest(AnnouncementCategory.Service, Named(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().PostAsJsonAsync($"{Base}/preview", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync($"{Base}/preview", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(Base)).StatusCode);
    }

    [Fact]
    public async Task AModeratorCannotAddressEveryone_ButCanWriteToNamedAccounts()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var toEveryone = await moderator.PostAsJsonAsync($"{Base}/preview", new PreviewAnnouncementRequest(AnnouncementCategory.Service, new AnnouncementAudienceRequest(AnnouncementAudience.All)));
        var toPushUsers = await moderator.PostAsJsonAsync($"{Base}/preview", new PreviewAnnouncementRequest(AnnouncementCategory.Service, new AnnouncementAudienceRequest(AnnouncementAudience.PushEnabled)));

        Assert.Equal(HttpStatusCode.Forbidden, toEveryone.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, toPushUsers.StatusCode);
        Assert.Equal(1, await PreviewAsync(moderator, AnnouncementCategory.Service, target.User.Id));
    }

    // ---------------------------------------------------------------- the whole trip

    [Fact]
    public async Task ASentAnnouncement_ReachesTheirInbox_TheirPhone_AndShowsInTheHistory()
    {
        var (ana, anaClient) = await RegisterAndAuthenticateAsync();
        var (bia, biaClient) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var token = $"ExponentPushToken[announce{Guid.NewGuid():N}]";
        await anaClient.PostAsJsonAsync("/api/v1/users/me/devices", new RegisterDeviceRequest(token, PushPlatform.Android));
        var title = $"Manutenção {Guid.NewGuid():N}"[..30];

        var expected = await PreviewAsync(admin, AnnouncementCategory.Service, ana.User.Id, bia.User.Id);
        var response = await SendAsync(admin, AnnouncementCategory.Service, expected, title, ana.User.Id, bia.User.Id);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<AnnouncementResponse>())!;
        Assert.Equal((2, AnnouncementStatus.Pending), (created.ExpectedRecipients, created.Status));
        Assert.NotNull(response.Headers.Location);

        var done = await WaitUntilSentAsync(admin, created.Id);
        Assert.Equal((AnnouncementStatus.Sent, 2), (done.Status, done.CreatedCount));

        // the inbox: the team's own content, the staff member hidden, counted as unread
        var item = Assert.Single(await InboxAsync(anaClient), n => n.Type == NotificationType.Announcement);
        Assert.Equal((title, "Texto do comunicado.", "Equipe Sunset", Guid.Empty, created.Id), (item.Title, item.Body, item.ActorName, item.ActorUserId, item.AnnouncementId));
        Assert.Equal(1, (await anaClient.GetFromJsonAsync<NotificationUnreadCountResponse>("/api/v1/notifications/unread-count"))!.Count);
        Assert.Single(await InboxAsync(biaClient), n => n.Type == NotificationType.Announcement);

        // the phone
        for (var i = 0; i < 100 && factory.PushSender.To(token).Count == 0; i++)
            await Task.Delay(100);
        var push = Assert.Single(factory.PushSender.To(token));
        Assert.Equal((title, "Texto do comunicado.", "Announcement", "Service"), (push.Title, push.Body, push.Data["type"], push.Data["category"]));
        Assert.Equal(created.Id, push.Data["announcementId"]);

        // reading it shows in the history
        (await anaClient.PostAsync($"/api/v1/notifications/{item.Id}/read", null)).EnsureSuccessStatusCode();
        var listed = (await admin.GetFromJsonAsync<CursorPagedResult<AnnouncementResponse>>(Base))!;
        Assert.Equal(1, listed.Items.Single(a => a.Id == created.Id).ReadCount);
    }

    [Fact]
    public async Task IfTheAudienceChangedSincePreview_NothingIsSent()
    {
        var (target, targetClient) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        var response = await SendAsync(admin, AnnouncementCategory.Service, expected: 5, "Teste", target.User.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("recipients", await response.Content.ReadAsStringAsync());
        await Task.Delay(2500);
        Assert.Empty(await InboxAsync(targetClient));
    }

    // ---------------------------------------------------------------- consent and caps

    [Fact]
    public async Task APromotionReachesOnlyThoseWhoOptedIn_AndNoMoreThanOnePerDay()
    {
        var (yes, yesClient) = await RegisterAndAuthenticateAsync();
        var (no, noClient) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        (await yesClient.PutAsJsonAsync("/api/v1/users/me/notification-settings", new UpdateNotificationSettingsRequest(true, null, PromotionalEnabled: true))).EnsureSuccessStatusCode();

        Assert.Equal(2, await PreviewAsync(admin, AnnouncementCategory.Service, yes.User.Id, no.User.Id));      // a service notice reaches both
        var expected = await PreviewAsync(admin, AnnouncementCategory.Promotional, yes.User.Id, no.User.Id);
        Assert.Equal(1, expected);                                                                              // a promotion, only the one who agreed

        var created = (await (await SendAsync(admin, AnnouncementCategory.Promotional, expected, "Promoção", yes.User.Id, no.User.Id)).Content.ReadFromJsonAsync<AnnouncementResponse>())!;
        await WaitUntilSentAsync(admin, created.Id);

        Assert.Single(await InboxAsync(yesClient), n => n.Type == NotificationType.Announcement);
        Assert.DoesNotContain(await InboxAsync(noClient), n => n.Type == NotificationType.Announcement);

        // a second promotion the same day reaches nobody, so it cannot even be sent
        Assert.Equal(0, await PreviewAsync(admin, AnnouncementCategory.Promotional, yes.User.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, AnnouncementCategory.Promotional, 0, "Outra promoção", yes.User.Id)).StatusCode);
        Assert.Equal(1, await PreviewAsync(admin, AnnouncementCategory.Service, yes.User.Id));                  // but a service notice still does
    }

    [Fact]
    public async Task ABannedAccountIsLeftOut()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/ban", new BanUserRequest("Spam repetido"))).EnsureSuccessStatusCode();

        Assert.Equal(0, await PreviewAsync(admin, AnnouncementCategory.Service, target.User.Id));
    }

    [Fact]
    public async Task TheConsentFlagIsPartOfTheSettings_OffUntilSaved()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var initial = (await client.GetFromJsonAsync<NotificationSettingsResponse>("/api/v1/users/me/notification-settings"))!;
        Assert.False(initial.PromotionalEnabled);
        Assert.DoesNotContain("Announcement", initial.Types.Keys);

        await client.PutAsJsonAsync("/api/v1/users/me/notification-settings", new UpdateNotificationSettingsRequest(true, null, PromotionalEnabled: true));
        Assert.True((await client.GetFromJsonAsync<NotificationSettingsResponse>("/api/v1/users/me/notification-settings"))!.PromotionalEnabled);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public async Task BadContent_Is400()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var audience = Named(target.User.Id);

        var externalLink = await admin.PostAsJsonAsync(Base, new CreateAnnouncementRequest("Oi", "Texto", AnnouncementCategory.Service, audience, 1, "https://evil.example"));
        var longTitle = await admin.PostAsJsonAsync(Base, new CreateAnnouncementRequest(new string('t', 66), "Texto", AnnouncementCategory.Service, audience, 1));
        var longBody = await admin.PostAsJsonAsync(Base, new CreateAnnouncementRequest("Oi", new string('b', 241), AnnouncementCategory.Service, audience, 1));
        var tooMany = await admin.PostAsJsonAsync($"{Base}/preview", new PreviewAnnouncementRequest(AnnouncementCategory.Service, Named(Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray())));
        var emptyList = await admin.PostAsJsonAsync($"{Base}/preview", new PreviewAnnouncementRequest(AnnouncementCategory.Service, Named()));
        var badCategory = await admin.PostAsJsonAsync(Base, new { title = "Oi", body = "Texto", category = "Spam", audience = new { type = "Users", userIds = new[] { target.User.Id } }, expectedRecipients = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, externalLink.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longBody.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
    }

    [Fact]
    public async Task AnUnknownAnnouncement_Is404()
    {
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Base}/{Guid.NewGuid()}")).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sunset.Application.DTOs.Devices;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.Push;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class PushPhase2EndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Devices = "/api/v1/users/me/devices";
    private const string Settings = "/api/v1/users/me/notification-settings";

    private static string NewToken(string marker = "dev") => $"ExponentPushToken[{marker}{Guid.NewGuid():N}]";

    private static RegisterDeviceRequest Register(string token) => new(token, PushPlatform.Android, null, "1.0.0", "pt-BR");

    private async Task<List<PushMessage>> WaitForPushAsync(string token, int expected = 1, int seconds = 15)
    {
        for (var i = 0; i < seconds * 10; i++)
        {
            var sent = factory.PushSender.To(token);
            if (sent.Count >= expected)
                return sent;
            await Task.Delay(100);
        }

        return factory.PushSender.To(token);
    }

    private async Task<PhotoOf> NewPhotoAsync(HttpClient owner)
    {
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        return new PhotoOf(photo.Id);
    }

    private sealed record PhotoOf(Guid Id);

    // ---------------------------------------------------------------- preferences

    [Fact]
    public async Task TheSettings_StartAllOn_AreSavedWholesale_AndNeedLogin()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().GetAsync(Settings)).StatusCode);

        var initial = (await client.GetFromJsonAsync<NotificationSettingsResponse>(Settings))!;
        Assert.True(initial.PushEnabled);
        Assert.Equal(["NewFollower", "PhotoLiked", "PhotoCommented", "CommentReplied"], initial.Types.Keys);
        Assert.All(initial.Types.Values, Assert.True);

        var saved = await client.PutAsJsonAsync(Settings, new UpdateNotificationSettingsRequest(true, new() { ["PhotoLiked"] = false }));
        saved.EnsureSuccessStatusCode();
        var afterSave = (await client.GetFromJsonAsync<NotificationSettingsResponse>(Settings))!;
        Assert.False(afterSave.Types["PhotoLiked"]);
        Assert.True(afterSave.Types["NewFollower"]);

        // saving again replaces everything: the earlier mute is gone
        (await client.PutAsJsonAsync(Settings, new UpdateNotificationSettingsRequest(false))).EnsureSuccessStatusCode();
        var replaced = (await client.GetFromJsonAsync<NotificationSettingsResponse>(Settings))!;
        Assert.False(replaced.PushEnabled);
        Assert.True(replaced.Types["PhotoLiked"]);
    }

    [Fact]
    public async Task AnUnknownKind_Is400()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PutAsJsonAsync(Settings, new UpdateNotificationSettingsRequest(true, new() { ["PhotoShared"] = false }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PhotoShared", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMutedKind_SendsNoPush_ButTheNotificationIsStillInTheApp()
    {
        var (_, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var (_, followerClient) = await RegisterAndAuthenticateAsync("Bia Seguidora");
        var token = NewToken("muted");
        await ownerClient.PostAsJsonAsync(Devices, Register(token));
        await ownerClient.PutAsJsonAsync(Settings, new UpdateNotificationSettingsRequest(true, new() { ["PhotoLiked"] = false }));
        var photo = await NewPhotoAsync(ownerClient);
        var ownerId = (await WithDbAsync(db => db.PushDevices.AsNoTracking().FirstAsync(d => d.Token == token))).UserId;

        (await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null)).EnsureSuccessStatusCode();          // muted kind
        (await followerClient.PostAsync($"/api/v1/users/{ownerId}/follow", null)).EnsureSuccessStatusCode();         // kind still on

        var pushes = await WaitForPushAsync(token);
        Assert.Equal("Novo seguidor", Assert.Single(pushes).Title);    // only the follower's push
        var inApp = (await ownerClient.GetFromJsonAsync<Sunset.Application.Common.CursorPagedResult<Sunset.Application.DTOs.Notifications.NotificationResponse>>("/api/v1/notifications"))!;
        Assert.Contains(inApp.Items, n => n.Type == NotificationType.PhotoLiked);   // the in-app list is complete
    }

    [Fact]
    public async Task WithPushTurnedOff_NothingIsPushed()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, followerClient) = await RegisterAndAuthenticateAsync();
        var token = NewToken("off");
        await ownerClient.PostAsJsonAsync(Devices, Register(token));
        await ownerClient.PutAsJsonAsync(Settings, new UpdateNotificationSettingsRequest(false));

        (await followerClient.PostAsync($"/api/v1/users/{owner.User.Id}/follow", null)).EnsureSuccessStatusCode();

        await Task.Delay(2500);
        Assert.Empty(factory.PushSender.To(token));
    }

    // ---------------------------------------------------------------- grouping

    [Fact]
    public async Task ABurstOfLikes_IsOnePushNowAndOneGroupedPushAfterTheWindow()
    {
        var (_, ownerClient) = await RegisterAndAuthenticateAsync();
        var token = NewToken("burst");
        await ownerClient.PostAsJsonAsync(Devices, Register(token));
        var photo = await NewPhotoAsync(ownerClient);

        // Everyone signs up first, so the three likes land within moments of each other (well inside the window).
        var likers = new List<HttpClient>();
        foreach (var name in new[] { "Ana Um", "Bia Dois", "Caio Tres" })
            likers.Add((await RegisterAndAuthenticateAsync(name)).Client);
        foreach (var liker in likers)
            (await liker.PostAsync($"/api/v1/photos/{photo.Id}/likes", null)).EnsureSuccessStatusCode();

        var pushes = await WaitForPushAsync(token, expected: 2);

        Assert.Equal(2, pushes.Count);                                       // not three
        Assert.Equal("Ana Um curtiu sua foto", pushes[0].Body);              // the first goes at once, as always
        Assert.Equal("Bia Dois e mais 1 curtiram sua foto", pushes[1].Body); // the rest, once the window ended
        Assert.Equal("Novas curtidas", pushes[1].Title);
        Assert.Equal(2, pushes[1].Data["count"]);
        Assert.Equal($"Photo:{photo.Id}", pushes[1].Data["targetDescription"]);
    }

    // ---------------------------------------------------------------- the test push

    [Fact]
    public async Task TheTestPush_IsForAdminsOnly_AndNeedsLogin()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().PostAsync($"{Devices}/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"{Devices}/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Devices}/test", null)).StatusCode);
    }

    [Fact]
    public async Task TheTestPush_ReachesTheCallersDevices_AndRemovesADeadOne()
    {
        var (admin, adminClient) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var alive = NewToken("alive");
        var dead = NewToken("dead");
        await adminClient.PostAsJsonAsync(Devices, Register(alive));
        await adminClient.PostAsJsonAsync(Devices, Register(dead));

        var response = await adminClient.PostAsync($"{Devices}/test", null);

        var result = (await response.Content.ReadFromJsonAsync<PushTestResult>())!;
        Assert.Equal(new PushTestResult(2, 1, 1), result);
        Assert.Equal("Teste de notificação", Assert.Single(factory.PushSender.To(alive)).Title);
        var left = await WithDbAsync(db => db.PushDevices.AsNoTracking().Where(d => d.UserId == admin.User.Id).Select(d => d.Token).ToListAsync());
        Assert.Equal([alive], left);
    }

    [Fact]
    public async Task TheTestPush_WithoutDevices_ReachesNobody()
    {
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        var result = (await (await admin.PostAsync($"{Devices}/test", null)).Content.ReadFromJsonAsync<PushTestResult>())!;

        Assert.Equal(new PushTestResult(0, 0, 0), result);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sunset.Application.DTOs.Devices;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Push;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class PushEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Devices = "/api/v1/users/me/devices";

    private static string NewToken(string? marker = null) => $"ExponentPushToken[{marker ?? "dev"}{Guid.NewGuid():N}]";

    private static RegisterDeviceRequest Register(string token, Guid? install = null) =>
        new(token, PushPlatform.Android, install, "1.0.0", "pt-BR");

    private Task<List<PushDevice>> DevicesOfAsync(Guid userId) =>
        WithDbAsync(db => db.PushDevices.AsNoTracking().Where(d => d.UserId == userId).ToListAsync());

    private Task<PushDevice?> DeviceWithAsync(string token) =>
        WithDbAsync(db => db.PushDevices.AsNoTracking().FirstOrDefaultAsync(d => d.Token == token));

    // The push is sent by a background worker about a second after the notification: wait for it.
    private async Task<List<PushMessage>> WaitForPushAsync(string token, int expected = 1)
    {
        for (var i = 0; i < 100; i++)
        {
            var sent = factory.PushSender.To(token);
            if (sent.Count >= expected)
                return sent;
            await Task.Delay(100);
        }

        return factory.PushSender.To(token);
    }

    private async Task<PhotoResponse> NewPhotoOfAsync(HttpClient owner)
    {
        var location = await CreateLocationAsync(owner);
        return await CreatePhotoAsync(owner, location.Id);
    }

    // ---------------------------------------------------------------- registration

    [Fact]
    public async Task RegisteringADevice_IsIdempotent_AndRequiresLogin()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();
        var token = NewToken();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().PostAsJsonAsync(Devices, Register(token))).StatusCode);

        var first = await client.PostAsJsonAsync(Devices, Register(token));
        var second = await client.PostAsJsonAsync(Devices, Register(token));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var device = Assert.Single(await DevicesOfAsync(auth.User.Id));
        Assert.Equal((token, PushPlatform.Android, "1.0.0", "pt-BR"), (device.Token, device.Platform, device.AppVersion, device.Locale));
    }

    [Fact]
    public async Task APlatformSentAsText_AndABadToken_AreHandled()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var ios = await client.PostAsync(Devices, JsonContent.Create(new { token = NewToken(), platform = "ios" }));
        var badToken = await client.PostAsJsonAsync(Devices, new { token = "not-a-token", platform = "android" });
        var badPlatform = await client.PostAsJsonAsync(Devices, new { token = NewToken(), platform = "windows-phone" });

        Assert.Equal(HttpStatusCode.NoContent, ios.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        Assert.Contains("token", await badToken.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, badPlatform.StatusCode);
    }

    [Fact]
    public async Task ATokenRegisteredByAnotherAccount_ChangesOwner()
    {
        var (ana, anaClient) = await RegisterAndAuthenticateAsync();
        var (bia, biaClient) = await RegisterAndAuthenticateAsync();
        var token = NewToken();
        await anaClient.PostAsJsonAsync(Devices, Register(token));

        (await biaClient.PostAsJsonAsync(Devices, Register(token))).EnsureSuccessStatusCode();

        Assert.Empty(await DevicesOfAsync(ana.User.Id));
        Assert.Equal(token, Assert.Single(await DevicesOfAsync(bia.User.Id)).Token);
    }

    [Fact]
    public async Task ARotatedTokenOfTheSameInstall_ReplacesTheOldRow()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();
        var install = Guid.NewGuid();
        var oldToken = NewToken();
        var newToken = NewToken();
        await client.PostAsJsonAsync(Devices, Register(oldToken, install));

        await client.PostAsJsonAsync(Devices, Register(newToken, install));

        Assert.Equal(newToken, Assert.Single(await DevicesOfAsync(auth.User.Id)).Token);
    }

    [Fact]
    public async Task AUserKeepsAtMostFiveDevices_TheLeastRecentlySeenIsDropped()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();
        var tokens = Enumerable.Range(0, 6).Select(_ => NewToken()).ToList();

        foreach (var token in tokens)
        {
            (await client.PostAsJsonAsync(Devices, Register(token))).EnsureSuccessStatusCode();
            await Task.Delay(20);
        }

        var kept = (await DevicesOfAsync(auth.User.Id)).Select(d => d.Token).ToList();
        Assert.Equal(5, kept.Count);
        Assert.DoesNotContain(tokens[0], kept);
        Assert.Contains(tokens[5], kept);
    }

    [Fact]
    public async Task RemovingADevice_IsIdempotent_AndOnlyTouchesTheCallersOwn()
    {
        var (ana, anaClient) = await RegisterAndAuthenticateAsync();
        var (_, biaClient) = await RegisterAndAuthenticateAsync();
        var anas = NewToken();
        var bias = NewToken();
        await anaClient.PostAsJsonAsync(Devices, Register(anas));
        await biaClient.PostAsJsonAsync(Devices, Register(bias));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(anaClient, new RemoveDeviceRequest(Token: bias))).StatusCode);   // not hers
        Assert.NotNull(await DeviceWithAsync(bias));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(anaClient, new RemoveDeviceRequest(Token: anas))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(anaClient, new RemoveDeviceRequest(Token: anas))).StatusCode);   // again: fine
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAsync(anaClient, new RemoveDeviceRequest())).StatusCode);              // needs something
        Assert.Empty(await DevicesOfAsync(ana.User.Id));
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, RemoveDeviceRequest body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Devices) { Content = JsonContent.Create(body) });

    // ---------------------------------------------------------------- sending

    [Fact]
    public async Task ALike_PushesToTheOwnersDevice_NotToTheAuthor()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync("Dono");
        var (liker, likerClient) = await RegisterAndAuthenticateAsync("Ana Curtidora");
        var ownerToken = NewToken("owner");
        var likerToken = NewToken("liker");
        await ownerClient.PostAsJsonAsync(Devices, Register(ownerToken));
        await likerClient.PostAsJsonAsync(Devices, Register(likerToken));
        var photo = await NewPhotoOfAsync(ownerClient);

        (await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null)).EnsureSuccessStatusCode();

        var push = Assert.Single(await WaitForPushAsync(ownerToken));
        Assert.Equal(("Nova curtida", "Ana Curtidora curtiu sua foto"), (push.Title, push.Body));
        Assert.Equal("PhotoLiked", push.Data["type"]);
        Assert.Equal($"Photo:{photo.Id}", push.Data["targetDescription"]);
        Assert.Equal(liker.User.Id, push.Data["actorUserId"]);
        Assert.True(push.Badge >= 1);                     // the unread count after this notification
        await Task.Delay(1500);
        Assert.Empty(factory.PushSender.To(likerToken));  // the author of the action gets nothing
    }

    [Fact]
    public async Task EveryDeviceOfTheRecipientIsNotified()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, followerClient) = await RegisterAndAuthenticateAsync("Bia Seguidora");
        var phone = NewToken("phone");
        var tablet = NewToken("tablet");
        await ownerClient.PostAsJsonAsync(Devices, Register(phone));
        await ownerClient.PostAsJsonAsync(Devices, Register(tablet));

        (await followerClient.PostAsync($"/api/v1/users/{owner.User.Id}/follow", null)).EnsureSuccessStatusCode();

        var onPhone = Assert.Single(await WaitForPushAsync(phone));
        Assert.Equal(("Novo seguidor", "Bia Seguidora começou a seguir você"), (onPhone.Title, onPhone.Body));
        Assert.Single(await WaitForPushAsync(tablet));
    }

    [Fact]
    public async Task ADeadToken_ReportedByTheProvider_IsRemoved()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var dead = NewToken("dead");
        var alive = NewToken("alive");
        await ownerClient.PostAsJsonAsync(Devices, Register(dead));
        await ownerClient.PostAsJsonAsync(Devices, Register(alive));
        var photo = await NewPhotoOfAsync(ownerClient);

        await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);
        await WaitForPushAsync(alive);

        for (var i = 0; i < 50 && await DeviceWithAsync(dead) is not null; i++)
            await Task.Delay(100);
        Assert.Null(await DeviceWithAsync(dead));
        Assert.NotNull(await DeviceWithAsync(alive));
    }

    [Fact]
    public async Task AcceptedPushes_LeaveATicket_ForTheReceiptCheck()
    {
        var (_, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var token = NewToken("ticketed");
        await ownerClient.PostAsJsonAsync(Devices, Register(token));
        var photo = await NewPhotoOfAsync(ownerClient);

        await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);
        await WaitForPushAsync(token);

        PushTicket? ticket = null;
        for (var i = 0; i < 50 && ticket is null; i++)
        {
            ticket = await WithDbAsync(db => db.PushTickets.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token));
            if (ticket is null) await Task.Delay(100);
        }

        Assert.NotNull(ticket);
        Assert.StartsWith("fake-ticket-", ticket!.ProviderTicketId);
    }

    // ---------------------------------------------------------------- clean-up

    [Fact]
    public async Task DeletingTheAccount_RemovesItsDevices()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();
        await client.PostAsJsonAsync(Devices, Register(NewToken()));
        await client.PostAsJsonAsync(Devices, Register(NewToken()));

        (await client.DeleteAsync("/api/v1/users/me")).EnsureSuccessStatusCode();

        Assert.Empty(await DevicesOfAsync(auth.User.Id));
    }

    [Fact]
    public async Task ABan_RemovesTheDevices_AndNothingIsPushedToTheBannedAccount()
    {
        var (target, targetClient) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var (_, otherClient) = await RegisterAndAuthenticateAsync();
        var token = NewToken("banned");
        await targetClient.PostAsJsonAsync(Devices, Register(token));

        (await moderator.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/ban", new BanUserRequest("Spam repetido"))).EnsureSuccessStatusCode();
        (await otherClient.PostAsync($"/api/v1/users/{target.User.Id}/follow", null)).EnsureSuccessStatusCode();   // a notification for the banned account

        Assert.Empty(await DevicesOfAsync(target.User.Id));
        await Task.Delay(1800);
        Assert.Empty(factory.PushSender.To(token));
    }

    [Fact]
    public async Task AnonymizingByModeration_RemovesTheDevices()
    {
        var (target, targetClient) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        await targetClient.PostAsJsonAsync(Devices, Register(NewToken()));

        (await admin.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/anonymize", new AnonymizeUserRequest("Conteudo ilegal"))).EnsureSuccessStatusCode();

        Assert.Empty(await DevicesOfAsync(target.User.Id));
    }
}

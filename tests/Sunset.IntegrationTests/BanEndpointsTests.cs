using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class BanEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Ban_BlocksLoginRefreshAndExistingToken_ThenUnbanRestores()
    {
        var email = UniqueEmail();
        var (target, targetClient) = await RegisterAndAuthenticateAsync(email: email);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var ban = await moderator.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/ban", new BanUserRequest("Spam repetido"));
        Assert.Equal(HttpStatusCode.NoContent, ban.StatusCode);

        // Already-issued access token stops working immediately (not only after it expires).
        var me = await targetClient.GetAsync("/api/v1/notifications/unread-count");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var login = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains("Spam repetido", await login.Content.ReadAsStringAsync());

        var refresh = await CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(target.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        var unban = await moderator.DeleteAsync($"/api/v1/moderation/users/{target.User.Id}/ban");
        Assert.Equal(HttpStatusCode.NoContent, unban.StatusCode);

        var loginAgain = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, loginAgain.StatusCode);
    }

    [Fact]
    public async Task Ban_ByRegularUser_IsForbidden()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, regular) = await RegisterAndAuthenticateAsync();

        var response = await regular.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/ban", new BanUserRequest("Spam"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ban_ModeratorBanningModerator_IsForbidden()
    {
        var (peer, _) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PostAsJsonAsync($"/api/v1/moderation/users/{peer.User.Id}/ban", new BanUserRequest("Spam"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ban_WithoutReason_ReturnsBadRequest()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/ban", new BanUserRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

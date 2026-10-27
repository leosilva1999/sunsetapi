using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Users;
using Sunset.Domain.Entities;
using Sunset.Infrastructure.Persistence;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class ChangePasswordEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Url = "/api/v1/users/me/password";
    private const string OldPassword = "Password123!";
    private const string NewPassword = "Brand-new-pass-456";

    private readonly SunsetApiFactory _factory = factory;

    private async Task<AuthResponse> LoginAsync(string email, string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(refreshToken));

    [Fact]
    public async Task ChangePassword_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await CreateClient().PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Succeeds_SwitchesLoginPasswordAndKeepsOnlyTheCurrentSession()
    {
        var email = UniqueEmail();
        var (thisDevice, client) = await RegisterAndAuthenticateAsync(email: email);
        var otherDevice = await LoginAsync(email, OldPassword);

        var response = await client.PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, NewPassword, thisDevice.RefreshToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var oldLogin = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, OldPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        await LoginAsync(email, NewPassword);

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(thisDevice.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherDevice.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithoutRefreshToken_RevokesEverySession()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SendsNoticeEmailToTheAccount()
    {
        var email = UniqueEmail();
        var (_, client) = await RegisterAndAuthenticateAsync(email: email);

        await client.PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.True(_factory.EmailSender.TryGetSentBody(email, out var body));
        Assert.Contains("foi alterada", body);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsValidationErrorAndChangesNothing()
    {
        var email = UniqueEmail();
        var (auth, client) = await RegisterAndAuthenticateAsync(email: email);

        var response = await client.PutAsJsonAsync(Url, new ChangePasswordRequest("Wrong-password-1", NewPassword, auth.RefreshToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("CurrentPassword", out _));
        await LoginAsync(email, OldPassword);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(auth.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithNewPasswordEqualToCurrent_ReturnsValidationError()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, OldPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("NewPassword", out _));
    }

    [Fact]
    public async Task ChangePassword_WithShortNewPassword_ReturnsValidationError()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_OnAccountWithoutPassword_ReturnsConflict()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SunsetDbContext>();
            var user = await db.Users.FirstAsync(u => u.Id == auth.User.Id);
            db.Entry(user).Property(nameof(User.HasPassword)).CurrentValue = false;
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(Url, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsHasPasswordTrueInTheUserPayload()
    {
        var (auth, _) = await RegisterAndAuthenticateAsync();

        Assert.True(auth.User.HasPassword);
    }
}

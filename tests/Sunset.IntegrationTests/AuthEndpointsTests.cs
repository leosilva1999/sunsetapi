using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Auth;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class AuthEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_WithNewEmail_ReturnsTokensAndUser()
    {
        var client = CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", email, "Password123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal(email, auth.User.Email);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var request = new RegisterRequest("Ana Silva", email, "Password123!");

        await client.PostAsJsonAsync("/api/v1/auth/register", request);
        var second = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", UniqueEmail(), "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsTokens()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", email, "Password123!"));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", email, "Password123!"));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "WrongPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_IssuesNewAccessToken()
    {
        var client = CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", UniqueEmail(), "Password123!"));
        var auth = (await register.Content.ReadFromJsonAsync<AuthResponse>())!;

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(auth.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshed);
        Assert.NotEqual(auth.RefreshToken, refreshed!.RefreshToken);
    }

    [Fact]
    public async Task Refresh_AfterLogout_IsRejected()
    {
        var client = CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", UniqueEmail(), "Password123!"));
        var auth = (await register.Content.ReadFromJsonAsync<AuthResponse>())!;

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshTokenRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}

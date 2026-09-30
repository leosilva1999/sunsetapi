using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Sunset.Application.DTOs.Auth;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class PasswordResetEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static string ExtractResetToken(string htmlBody)
    {
        var match = Regex.Match(htmlBody, "token=([^\"&\\s]+)");
        Assert.True(match.Success, $"No reset token found in email body: {htmlBody}");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task ForgotPassword_WithExistingEmail_SendsEmailWithResetLink()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", email, "Password123!"));

        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(factory.EmailSender.TryGetSentBody(email, out var body));
        Assert.Contains("token=", body);
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_ReturnsNoContentWithoutSendingEmail()
    {
        var client = CreateClient();
        var unknownEmail = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(unknownEmail));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(factory.EmailSender.TryGetSentBody(unknownEmail, out _));
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_AllowsLoginWithNewPasswordAndRevokesOldSessions()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", email, "Password123!"));
        var originalAuth = (await register.Content.ReadFromJsonAsync<AuthResponse>())!;

        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        factory.EmailSender.TryGetSentBody(email, out var body);
        var token = ExtractResetToken(body);

        var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "NewPassword456!"));
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var oldPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "NewPassword456!"));
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);

        var oldRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(originalAuth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest("not-a-real-token", "NewPassword456!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_TokenIsSingleUse()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana Silva", email, "Password123!"));

        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        factory.EmailSender.TryGetSentBody(email, out var body);
        var token = ExtractResetToken(body);

        var first = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "NewPassword456!"));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "AnotherPassword789!"));
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithWeakNewPassword_ReturnsBadRequest()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest("some-token", "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Legal;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LegalAcceptanceEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    // Document versions are shared by the whole test run, so each test publishes its own new
    // version through the API and works with whatever number comes back.
    private async Task<(int Terms, int Privacy)> PublishNewVersionsAsync()
    {
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var terms = await (await admin.PutAsJsonAsync("/api/v1/terms", new UpdateLegalDocumentRequest("Termos de teste"))).Content.ReadFromJsonAsync<LegalDocumentResponse>();
        var privacy = await (await admin.PutAsJsonAsync("/api/v1/privacy", new UpdateLegalDocumentRequest("Privacidade de teste"))).Content.ReadFromJsonAsync<LegalDocumentResponse>();
        return (terms!.Version, privacy!.Version);
    }

    [Fact]
    public async Task Register_WithCurrentVersions_IsNotPending()
    {
        var (terms, privacy) = await PublishNewVersionsAsync();
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana", UniqueEmail(), "Password123!", terms, privacy));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.False(auth.LegalAcceptance!.Pending);
        Assert.Equal(terms, auth.LegalAcceptance.Terms.AcceptedVersion);
        Assert.Equal(privacy, auth.LegalAcceptance.Privacy.AcceptedVersion);
    }

    [Fact]
    public async Task Register_WithoutVersions_IsPending_AndAcceptingClearsIt()
    {
        var (terms, privacy) = await PublishNewVersionsAsync();
        var (auth, client) = await RegisterAndAuthenticateAsync();
        Assert.True(auth.LegalAcceptance!.Pending);

        var accept = await client.PostAsJsonAsync("/api/v1/users/me/legal-acceptances", new AcceptLegalDocumentsRequest(terms, privacy));

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.False((await accept.Content.ReadFromJsonAsync<LegalAcceptanceStatusResponse>())!.Pending);
        var status = await client.GetFromJsonAsync<LegalAcceptanceStatusResponse>("/api/v1/users/me/legal-acceptances");
        Assert.False(status!.Pending);
    }

    [Fact]
    public async Task PublishingANewVersion_MakesTheUserPendingAgain()
    {
        var (terms, privacy) = await PublishNewVersionsAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana", email, "Password123!", terms, privacy));

        await PublishNewVersionsAsync();

        var login = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.True(auth.LegalAcceptance!.Pending);
        Assert.Equal(terms, auth.LegalAcceptance.Terms.AcceptedVersion);
        Assert.True(auth.LegalAcceptance.Terms.CurrentVersion > terms);
    }

    [Fact]
    public async Task Register_WithAStaleVersion_ReturnsBadRequest()
    {
        var (terms, privacy) = await PublishNewVersionsAsync();

        var response = await CreateClient().PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("Ana", UniqueEmail(), "Password123!", terms - 1, privacy));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Accept_WithAStaleVersion_ReturnsBadRequest()
    {
        var (terms, privacy) = await PublishNewVersionsAsync();
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync("/api/v1/users/me/legal-acceptances", new AcceptLegalDocumentsRequest(terms - 1, privacy));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LegalAcceptances_WithoutLogin_ReturnUnauthorized()
    {
        var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me/legal-acceptances")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/users/me/legal-acceptances", new AcceptLegalDocumentsRequest(1, 1))).StatusCode);
    }
}

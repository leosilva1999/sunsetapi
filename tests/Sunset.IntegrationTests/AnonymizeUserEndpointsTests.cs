using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Users;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class AnonymizeUserEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Anonymize_ByAdmin_ErasesAccountAndBlocksLogin()
    {
        var email = UniqueEmail();
        var (target, targetClient) = await RegisterAndAuthenticateAsync(email: email);
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        var response = await admin.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/anonymize", new AnonymizeUserRequest("Conteudo ilegal"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var profile = await CreateClient().GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{target.User.Id}");
        Assert.Equal("Usuário excluído", profile!.Name);

        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/v1/notifications/unread-count")).StatusCode);
        var login = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Anonymize_WithHideContent_HidesThePhotos()
    {
        var (target, targetClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(targetClient);
        var photo = await CreatePhotoAsync(targetClient, location.Id);
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        await admin.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/anonymize", new AnonymizeUserRequest("Spam", HideContent: true));

        Assert.Equal(HttpStatusCode.NotFound, (await CreateClient().GetAsync($"/api/v1/photos/{photo.Id}")).StatusCode);
    }

    [Fact]
    public async Task Anonymize_ByModerator_IsForbidden()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/anonymize", new AnonymizeUserRequest("Spam"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymize_Twice_ReturnsConflict()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        await admin.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/anonymize", new AnonymizeUserRequest("Spam"));
        var again = await admin.PostAsJsonAsync($"/api/v1/moderation/users/{target.User.Id}/anonymize", new AnonymizeUserRequest("Spam"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}

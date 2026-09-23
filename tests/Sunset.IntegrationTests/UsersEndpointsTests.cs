using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class UsersEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetById_ReturnsTheUser()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync(name: "Ana Silva");

        var response = await client.GetAsync($"/api/v1/users/{auth.User.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Ana Silva", user!.Name);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ReturnsNotFound()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMe_WithOnlyBioSet_LeavesNameUnchanged()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync(name: "Ana Silva");

        var response = await client.PatchAsJsonAsync("/api/v1/users/me", new { bio = "Nova bio" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Ana Silva", updated!.Name);
        Assert.Equal("Nova bio", updated.Bio);

        var refetched = await client.GetFromJsonAsync<UserResponse>($"/api/v1/users/{auth.User.Id}");
        Assert.Equal("Nova bio", refetched!.Bio);
    }

    [Fact]
    public async Task UpdateMe_WithBioExplicitlyNull_ClearsIt()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        await client.PatchAsJsonAsync("/api/v1/users/me", new { bio = "Bio temporaria" });

        var response = await client.PatchAsJsonAsync("/api/v1/users/me", new { bio = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Null(updated!.Bio);
    }

    [Fact]
    public async Task UpdateMe_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.PatchAsJsonAsync("/api/v1/users/me", new { bio = "Nova bio" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMe_WithBioTooLong_ReturnsBadRequest()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/users/me", new { bio = new string('a', 161) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAvatarUploadUrl_ReturnsPutUrlAndFinalAvatarUrl()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync("/api/v1/users/me/avatar-upload-url", new CreateAvatarUploadUrlRequest("image/png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AvatarUploadUrlResponse>();
        Assert.StartsWith("http://localhost:4566/sunset-photos-test/avatars/", body!.UploadUrl);
        Assert.StartsWith("http://localhost:4566/sunset-photos-test/avatars/", body.AvatarUrl);
    }

    [Fact]
    public async Task CreateAvatarUploadUrl_WithUnsupportedContentType_ReturnsBadRequest()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync("/api/v1/users/me/avatar-upload-url", new CreateAvatarUploadUrlRequest("application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAvatarUploadUrl_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/users/me/avatar-upload-url", new CreateAvatarUploadUrlRequest("image/png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPhotos_ReturnsOnlyThatUsersPhotos()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/photos",
            new { locationId = location.Id, imageUrl = $"https://sunset-photos-test.s3.amazonaws.com/{Guid.NewGuid():N}.jpg", caption = (string?)null });
        createResponse.EnsureSuccessStatusCode();
        var photo = await createResponse.Content.ReadFromJsonAsync<PhotoResponse>();

        var response = await client.GetAsync($"/api/v1/users/{auth.User.Id}/photos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<PhotoResponse>>();
        Assert.Contains(page!.Items, p => p.Id == photo!.Id);
        Assert.All(page.Items, p => Assert.Equal(auth.User.Id, p.UserId));
    }
}

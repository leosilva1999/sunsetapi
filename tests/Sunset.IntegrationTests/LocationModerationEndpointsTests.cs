using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationModerationEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task DeleteLocation_HidesItAndItsPhotos_ThenRestoreBringsThemBack()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var delete = await moderator.DeleteAsync($"/api/v1/moderation/locations/{location.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/locations/{location.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/photos/{photo.Id}")).StatusCode);

        var deleted = await moderator.GetFromJsonAsync<List<DeletedLocationResponse>>("/api/v1/moderation/locations/deleted");
        Assert.Contains(deleted!, l => l.Id == location.Id);

        var restore = await moderator.PostAsync($"/api/v1/moderation/locations/{location.Id}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/locations/{location.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/photos/{photo.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restore_KeepsIndividuallyDeletedPhotosHidden()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var removedEarlier = await CreatePhotoAsync(owner, location.Id);
        var kept = await CreatePhotoAsync(owner, location.Id);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        await moderator.DeleteAsync($"/api/v1/photos/{removedEarlier.Id}");
        await moderator.DeleteAsync($"/api/v1/moderation/locations/{location.Id}");
        await moderator.PostAsync($"/api/v1/moderation/locations/{location.Id}/restore", null);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/photos/{removedEarlier.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/photos/{kept.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteLocation_ByRegularUser_IsForbidden()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);

        var response = await owner.DeleteAsync($"/api/v1/moderation/locations/{location.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Restore_OfLocationThatIsNotDeleted_ReturnsConflict()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PostAsync($"/api/v1/moderation/locations/{location.Id}/restore", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCoordinates_ByModerator_MovesThePin()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/coordinates", new UpdateLocationCoordinatesRequest(10.5, -20.25));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await owner.GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{location.Id}");
        Assert.Equal(10.5, updated!.Latitude);
        Assert.Equal(-20.25, updated.Longitude);
    }

    [Fact]
    public async Task UpdateCoordinates_ByRegularUser_IsForbidden()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);

        var response = await owner.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/coordinates", new UpdateLocationCoordinatesRequest(1, 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCoordinates_OutOfRange_ReturnsBadRequest()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/coordinates", new UpdateLocationCoordinatesRequest(95, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

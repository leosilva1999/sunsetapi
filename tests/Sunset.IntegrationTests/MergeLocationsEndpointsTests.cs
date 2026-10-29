using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Photos;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class MergeLocationsEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Merge_MovesPhotosAndRatings_AndRemovesTheSource()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var source = await CreateLocationAsync(owner);
        var target = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, source.Id);
        await owner.PostAsJsonAsync($"/api/v1/locations/{source.Id}/ratings", new CreateRatingRequest(5));
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PostAsJsonAsync($"/api/v1/moderation/locations/{source.Id}/merge", new MergeLocationsRequest(target.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var merged = (await response.Content.ReadFromJsonAsync<LocationResponse>())!;
        Assert.Equal(target.Id, merged.Id);
        Assert.Equal(5m, merged.AvgRating);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/locations/{source.Id}")).StatusCode);
        var moved = await owner.GetFromJsonAsync<PhotoResponse>($"/api/v1/photos/{photo.Id}");
        Assert.Equal(target.Id, moved!.LocationId);
    }

    [Fact]
    public async Task Merge_IntoItself_ReturnsConflict()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PostAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/merge", new MergeLocationsRequest(location.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Merge_ByRegularUser_IsForbidden()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var a = await CreateLocationAsync(owner);
        var b = await CreateLocationAsync(owner);

        var response = await owner.PostAsJsonAsync($"/api/v1/moderation/locations/{a.Id}/merge", new MergeLocationsRequest(b.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

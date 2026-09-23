using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationsEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_ThenGetById_ReturnsTheLocation()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var uniqueName = $"Praia do Rosa {Guid.NewGuid():N}";

        var location = await CreateLocationAsync(client, uniqueName);
        var getResponse = await client.GetAsync($"/api/v1/locations/{location.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<LocationResponse>();
        Assert.Equal(uniqueName, fetched!.Name);
        Assert.Equal(0m, fetched.AvgRating);
    }

    [Fact]
    public async Task Create_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest("Praia", -27, -48, "Floripa"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidLatitude_ReturnsBadRequest()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest("Praia", 999, -48, "Floripa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ReturnsNotFound()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_ByExactName_FindsTheLocation()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var token = Guid.NewGuid().ToString("N");
        var uniqueName = $"Farol{token}";
        await CreateLocationAsync(client, uniqueName);

        var response = await client.GetAsync($"/api/v1/locations?q={uniqueName}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>();
        Assert.Contains(page!.Items, l => l.Name == uniqueName);
    }

    [Fact]
    public async Task RateThenGetMine_ReturnsTheRating()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);

        var rateResponse = await client.PostAsJsonAsync($"/api/v1/locations/{location.Id}/ratings", new CreateRatingRequest(5, "Lindo demais"));
        Assert.Equal(HttpStatusCode.OK, rateResponse.StatusCode);

        var mineResponse = await client.GetAsync($"/api/v1/locations/{location.Id}/ratings/me");
        var mine = await mineResponse.Content.ReadFromJsonAsync<RatingResponse>();
        Assert.Equal(5, mine!.Score);
        Assert.Equal("Lindo demais", mine.Comment);

        var locationResponse = await client.GetAsync($"/api/v1/locations/{location.Id}");
        var updatedLocation = await locationResponse.Content.ReadFromJsonAsync<LocationResponse>();
        Assert.Equal(5m, updatedLocation!.AvgRating);
    }

    [Fact]
    public async Task Rate_WithScoreOutOfRange_ReturnsBadRequest()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);

        var response = await client.PostAsJsonAsync($"/api/v1/locations/{location.Id}/ratings", new CreateRatingRequest(6));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRating_RemovesIt()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);
        await client.PostAsJsonAsync($"/api/v1/locations/{location.Id}/ratings", new CreateRatingRequest(4));

        var deleteResponse = await client.DeleteAsync($"/api/v1/locations/{location.Id}/ratings");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var mineResponse = await client.GetAsync($"/api/v1/locations/{location.Id}/ratings/me");
        Assert.Equal(HttpStatusCode.NotFound, mineResponse.StatusCode);
    }

    [Fact]
    public async Task GetRanking_OrdersByAverageScoreDescending()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var topLocation = await CreateLocationAsync(client, $"Top {Guid.NewGuid():N}");
        var lowLocation = await CreateLocationAsync(client, $"Low {Guid.NewGuid():N}");

        await client.PostAsJsonAsync($"/api/v1/locations/{topLocation.Id}/ratings", new CreateRatingRequest(5));
        await client.PostAsJsonAsync($"/api/v1/locations/{lowLocation.Id}/ratings", new CreateRatingRequest(1));

        var response = await client.GetAsync("/api/v1/locations/ranking?period=all&limit=50");
        var ranking = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();

        var topIndex = ranking!.FindIndex(l => l.Id == topLocation.Id);
        var lowIndex = ranking.FindIndex(l => l.Id == lowLocation.Id);
        Assert.True(topIndex >= 0 && lowIndex >= 0 && topIndex < lowIndex);
    }
}

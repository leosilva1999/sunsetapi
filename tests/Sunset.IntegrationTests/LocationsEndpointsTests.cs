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

    // Nearby tests each use their own remote origin (nothing else in the suite creates locations
    // up there) so rows from other tests sharing the container can't leak into the radius.
    private static async Task<LocationResponse> CreateLocationAtAsync(HttpClient client, double lat, double lng)
    {
        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest($"Nearby {Guid.NewGuid():N}", lat, lng, "Nearby City"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationResponse>())!;
    }

    [Fact]
    public async Task Search_Nearby_ReturnsOnlyLocationsWithinRadiusOrderedByDistance()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var far = await CreateLocationAtAsync(client, 60.30, 100.0);   // ~33 km north
        var near = await CreateLocationAtAsync(client, 60.05, 100.0);  // ~5.6 km north
        var outside = await CreateLocationAtAsync(client, 61.50, 100.0); // ~167 km north

        var response = await client.GetAsync("/api/v1/locations?lat=60&lng=100&radius=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>();
        var ids = page!.Items.Select(l => l.Id).ToList();
        Assert.Equal(new[] { near.Id, far.Id }, ids);
        Assert.DoesNotContain(outside.Id, ids);
        Assert.InRange(page.Items[0].DistanceKm!.Value, 5.0, 6.2);
        Assert.InRange(page.Items[1].DistanceKm!.Value, 32.0, 34.5);
    }

    [Fact]
    public async Task Search_Nearby_ExcludesBoundingBoxCornersOutsideTheRadius()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        // Inside the 100 km bounding box (0.85 deg lat, 1.8 deg lng) but ~133 km away: a corner.
        var corner = await CreateLocationAtAsync(client, 62.85, 111.8);
        var center = await CreateLocationAtAsync(client, 62.0, 110.0);

        var response = await client.GetAsync("/api/v1/locations?lat=62&lng=110&radius=100");

        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>();
        Assert.Contains(page!.Items, l => l.Id == center.Id);
        Assert.DoesNotContain(page.Items, l => l.Id == corner.Id);
    }

    [Fact]
    public async Task Search_Nearby_PaginatesByDistanceWithoutRepeatsOrGaps()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var expected = new List<Guid>();
        foreach (var offset in new[] { 0.01, 0.02, 0.03, 0.04, 0.05 })
            expected.Add((await CreateLocationAtAsync(client, 63 + offset, 120.0)).Id);

        var collected = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/api/v1/locations?lat=63&lng=120&radius=20&limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await (await client.GetAsync(url)).Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>();
            collected.AddRange(page!.Items.Select(l => l.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(expected, collected);
    }

    [Theory]
    [InlineData("lat=60")]
    [InlineData("lng=100")]
    [InlineData("radius=10")]
    [InlineData("lat=60&lng=100&radius=0")]
    [InlineData("lat=60&lng=100&radius=201")]
    [InlineData("lat=91&lng=100")]
    [InlineData("lat=60&lng=181")]
    public async Task Search_WithInvalidGeoParameters_ReturnsBadRequest(string queryString)
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/locations?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithoutCoordinates_OmitsDistance()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var name = $"NoGeo{Guid.NewGuid():N}";
        await CreateLocationAsync(client, name);

        var page = await (await client.GetAsync($"/api/v1/locations?q={name}")).Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>();

        Assert.Null(Assert.Single(page!.Items).DistanceKm);
    }
}

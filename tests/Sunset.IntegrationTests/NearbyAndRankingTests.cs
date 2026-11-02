using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class NearbyAndRankingTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static int _slot;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<City> NewMunicipalityAsync(string tag, string state = "ZR") =>
        await WithDbAsync(async db =>
        {
            var city = new City(CityKind.Municipality, $"Rank{tag}", $"Rank{tag}", null, $"rank{tag}", "XX", state, $"rank{tag}-{state.ToLowerInvariant()}", "test", null, null, null, -20, -40, "UTC", 0);
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            return city;
        });

    private static async Task<LocationResponse> CreateAsync(HttpClient client, string name, City city, double lat, double lng) =>
        (await (await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, lat, lng, CityId: city.Id))).Content.ReadFromJsonAsync<LocationResponse>())!;

    private static async Task RateAsync(HttpClient client, Guid locationId, int score) =>
        (await client.PostAsJsonAsync($"/api/v1/locations/{locationId}/ratings", new CreateRatingRequest(score, null))).EnsureSuccessStatusCode();

    // ---------------------------------------------------------------- nearby

    [Fact]
    public async Task Nearby_ListsTheOtherPlacesAroundALocation_NearestFirst_WithoutItself()
    {
        var city = await NewMunicipalityAsync(Tag());
        var (_, client) = await RegisterAndAuthenticateAsync();
        var lng = -120.0 + 6.0 * (Interlocked.Increment(ref _slot) % 10);   // open ocean, apart from the other tests
        var center = await CreateAsync(client, $"Centro {Tag()}", city, -45.0, lng);
        var close = await CreateAsync(client, $"Perto {Tag()}", city, -45.0, lng + 0.05);    // ~4 km
        var farther = await CreateAsync(client, $"Longe {Tag()}", city, -45.0, lng + 0.2);   // ~16 km
        var outside = await CreateAsync(client, $"Fora {Tag()}", city, -45.0, lng + 1.5);    // ~118 km

        var byId = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/{center.Id}/nearby"))!;
        var bySlug = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/{center.Slug}/nearby?radiusKm=30&limit=1"))!;
        var wide = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/{center.Id}/nearby?radiusKm=150"))!;

        Assert.Equal([close.Id, farther.Id], byId.Select(l => l.Id));       // default 30 km: not the far one, not itself
        Assert.InRange(byId[0].DistanceKm!.Value, 3, 5);
        Assert.InRange(byId[1].DistanceKm!.Value, 14, 18);
        Assert.Equal([close.Id], bySlug.Select(l => l.Id));                  // limit=1, found by slug
        Assert.Equal([close.Id, farther.Id, outside.Id], wide.Select(l => l.Id));
    }

    [Fact]
    public async Task Nearby_HidesRemovedLocations_Is404ForUnknownOnes_And400ForABadRadius()
    {
        var city = await NewMunicipalityAsync(Tag());
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var lng = -120.0 + 6.0 * (Interlocked.Increment(ref _slot) % 10);
        var center = await CreateAsync(client, $"Centro {Tag()}", city, -47.0, lng);
        var gone = await CreateAsync(client, $"Removido {Tag()}", city, -47.0, lng + 0.05);
        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{gone.Id}")).EnsureSuccessStatusCode();

        var nearby = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/{center.Id}/nearby"))!;

        Assert.Empty(nearby);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateClient().GetAsync("/api/v1/locations/nao-existe-mesmo/nearby")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateClient().GetAsync($"/api/v1/locations/{center.Id}/nearby?radiusKm=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateClient().GetAsync($"/api/v1/locations/{center.Id}/nearby?radiusKm=500")).StatusCode);
    }

    // ---------------------------------------------------------------- location ranking filters

    [Fact]
    public async Task TheLocationRanking_CanBeNarrowedToACityAndToAState()
    {
        var tag = Tag();
        var state = $"Y{(char)('A' + Random.Shared.Next(26))}";   // no other test uses a Y* state
        var inCity = await NewMunicipalityAsync(tag, state);
        var elsewhere = await NewMunicipalityAsync(Tag(), "ZQ");
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (_, rater) = await RegisterAndAuthenticateAsync();
        var best = await CreateAsync(owner, $"Melhor {Tag()}", inCity, -20, -40);
        var ok = await CreateAsync(owner, $"Mediano {Tag()}", inCity, -20, -40.1);
        var other = await CreateAsync(owner, $"Outra {Tag()}", elsewhere, -20, -40.2);
        await RateAsync(rater, best.Id, 5);
        await RateAsync(rater, ok.Id, 3);
        await RateAsync(rater, other.Id, 5);

        var byCity = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/ranking?citySlug={inCity.Slug}"))!;
        var byState = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/ranking?state={state.ToLowerInvariant()}"))!;
        var both = (await CreateClient().GetFromJsonAsync<List<LocationResponse>>($"/api/v1/locations/ranking?citySlug={elsewhere.Slug}&state={state}"))!;

        Assert.Equal([best.Id, ok.Id], byCity.Select(l => l.Id));                // only that city, best first
        Assert.Equal([best.Id, ok.Id], byState.Select(l => l.Id));               // the other city is in another state
        Assert.Empty(both);                                                       // AND: that city is not in this state
        Assert.Equal(HttpStatusCode.NotFound, (await CreateClient().GetAsync("/api/v1/locations/ranking?citySlug=nao-existe-xx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateClient().GetAsync("/api/v1/locations/ranking?state=1%3B2")).StatusCode);
    }

    // ---------------------------------------------------------------- cities ranking

    [Fact]
    public async Task TheCitiesRanking_OrdersMunicipalitiesByTheRatingsTheyReceived()
    {
        var good = await NewMunicipalityAsync(Tag());
        var meh = await NewMunicipalityAsync(Tag());
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (_, raterA) = await RegisterAndAuthenticateAsync();
        var (_, raterB) = await RegisterAndAuthenticateAsync();
        var a = await CreateAsync(owner, $"A {Tag()}", good, -20, -40);
        var b = await CreateAsync(owner, $"B {Tag()}", good, -20, -40.1);
        var c = await CreateAsync(owner, $"C {Tag()}", meh, -20, -40.2);
        await RateAsync(raterA, a.Id, 5);
        await RateAsync(raterB, b.Id, 4);    // good: (5 + 4) / 2 = 4.5
        await RateAsync(raterA, c.Id, 2);    // meh: 2.0

        var ranking = (await CreateClient().GetFromJsonAsync<List<CityRankingResponse>>("/api/v1/cities/ranking?limit=50"))!;

        var goodEntry = ranking.Single(r => r.City.Slug == good.Slug);
        var mehEntry = ranking.Single(r => r.City.Slug == meh.Slug);
        Assert.Equal((4.5m, 2), (goodEntry.PeriodAvgRating, goodEntry.PeriodRatingsCount));
        Assert.Equal((2m, 1), (mehEntry.PeriodAvgRating, mehEntry.PeriodRatingsCount));
        Assert.True(ranking.IndexOf(goodEntry) < ranking.IndexOf(mehEntry));
        Assert.Equal(2, goodEntry.City.LocationsCount);                           // the usual counts travel along
        Assert.True(ranking.All(r => r.City.Kind == CityKind.Municipality));
    }

    [Fact]
    public async Task TheCitiesRanking_AcceptsThePeriods_AndRejectsAnUnknownOne()
    {
        foreach (var period in new[] { "week", "month", "all" })
            Assert.Equal(HttpStatusCode.OK, (await CreateClient().GetAsync($"/api/v1/cities/ranking?period={period}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateClient().GetAsync("/api/v1/cities/ranking?period=year")).StatusCode);
    }
}

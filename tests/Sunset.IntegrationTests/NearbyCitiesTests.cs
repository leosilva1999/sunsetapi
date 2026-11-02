using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Cities;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class NearbyCitiesTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static int _slot;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // Open ocean, far from any real catalog entry: only the places inserted here can be nearby. Each seeding gets
    // its own spot, 12 degrees of longitude apart (the search never looks beyond ~250 km), so tests cannot see each other's cities.
    private async Task<(double Lat, double Lng, City Town, City Village, City Faraway)> SeedAsync()
    {
        var tag = Tag();
        var (lat, lng) = (-40.0, -170.0 + 12.0 * (Interlocked.Increment(ref _slot) % 14));
        return await WithDbAsync(async db =>
        {
            var town = new City(CityKind.Municipality, $"Vila{tag}", $"Vila{tag}", null, $"vila{tag}", "XX", "ZN", $"vila{tag}-zn", "test", null, null, null, lat + 0.09, lng, "UTC", 0);   // ~10 km
            db.Cities.Add(town);
            await db.SaveChangesAsync();
            var village = new City(CityKind.Locality, $"Praia{tag}", $"Praia{tag}", null, $"praia{tag}", "XX", "ZN", $"praia{tag}-vila{tag}-zn", "test", null, null, town.Id, lat + 0.009, lng, "UTC", 0);   // ~1 km
            var faraway = new City(CityKind.Municipality, $"Longe{tag}", $"Longe{tag}", null, $"longe{tag}", "XX", "ZN", $"longe{tag}-zn", "test", null, null, null, lat + 1.5, lng, "UTC", 0);   // ~167 km: past the 150 km warning, inside the widest (250 km) search
            db.Cities.AddRange(village, faraway);
            await db.SaveChangesAsync();
            return (lat, lng, town, village, faraway);
        });
    }

    private static string Url(double lat, double lng, string extra = "") =>
        FormattableString.Invariant($"/api/v1/geo/cities?lat={lat}&lng={lng}{extra}");

    [Fact]
    public async Task ThePlacesComeNearestFirst_WithDistance_AndALocalityCarriesItsMunicipality()
    {
        var (lat, lng, town, village, _) = await SeedAsync();

        var result = (await CreateClient().GetFromJsonAsync<NearbyCitiesResponse>(Url(lat, lng, "&limit=2")))!;

        Assert.Equal([village.Id, town.Id], result.Items.Select(i => i.Id));
        Assert.InRange(result.Items[0].DistanceKm, 0.5, 1.5);
        Assert.InRange(result.Items[1].DistanceKm, 8, 12);
        Assert.Equal(CityKind.Locality, result.Items[0].Kind);
        Assert.Equal(town.Slug, result.Items[0].Parent!.Slug);
        Assert.Null(result.Items[1].Parent);
        Assert.Null(result.ChosenCity);
    }

    [Fact]
    public async Task TheSearchWidens_UntilEnoughPlacesAreFound_AndTheLimitCaps()
    {
        var (lat, lng, _, _, faraway) = await SeedAsync();

        var three = (await CreateClient().GetFromJsonAsync<NearbyCitiesResponse>(Url(lat, lng, "&limit=3")))!;
        var one = (await CreateClient().GetFromJsonAsync<NearbyCitiesResponse>(Url(lat, lng, "&limit=1")))!;

        Assert.Equal(3, three.Items.Count);
        Assert.Equal(faraway.Id, three.Items[2].Id);   // only found once the area grew past the first radius
        Assert.Single(one.Items);
    }

    [Fact]
    public async Task APinFarFromTheChosenCity_GetsAWarning_ANearOneDoesNot()
    {
        var (lat, lng, town, _, faraway) = await SeedAsync();

        var near = (await CreateClient().GetFromJsonAsync<NearbyCitiesResponse>(Url(lat, lng, $"&cityId={town.Id}")))!;
        var far = (await CreateClient().GetFromJsonAsync<NearbyCitiesResponse>(Url(lat, lng, $"&cityId={faraway.Id}")))!;

        Assert.False(near.ChosenCity!.FarAway);
        Assert.True(far.ChosenCity!.FarAway);
        Assert.InRange(far.ChosenCity.DistanceKm, 160, 175);
    }

    [Fact]
    public async Task BadInput_Is400_AnUnknownChosenCityIs404_AndEmptySeaIsEmpty()
    {
        var client = CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/geo/cities")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/geo/cities?lat=95&lng=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(1, 1, $"&cityId={Guid.NewGuid()}"))).StatusCode);

        var empty = (await client.GetFromJsonAsync<NearbyCitiesResponse>(Url(-70.0, 170.0)))!;   // nothing within 250 km
        Assert.Empty(empty.Items);
    }
}

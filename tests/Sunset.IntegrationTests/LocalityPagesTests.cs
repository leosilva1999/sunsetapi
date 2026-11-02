using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Sitemap;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocalityPagesTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // A state code of its own per test, so nothing seeded elsewhere can show up in a list filtered by it.
    private static string NewState() => ("W" + Tag()[..6]).ToUpperInvariant();

    // Own client address per test: some of these list/search, and the suite otherwise looks like one client.
    private readonly string _clientIp = $"198.20.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    private Task<HttpResponseMessage> GetAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", _clientIp);
        return CreateClient().SendAsync(request);
    }

    private async Task<(City Town, City Village)> SeedAsync(string state)
    {
        var tag = Tag();
        return await WithDbAsync(async db =>
        {
            var town = new City(CityKind.Municipality, $"Vila{tag}", $"Vila{tag}", null, $"vila{tag}", "XX", state, $"vila{tag}-{state.ToLowerInvariant()}", "test", null, null, null, -20, -40, "America/Sao_Paulo", 0);
            db.Cities.Add(town);
            await db.SaveChangesAsync();
            var village = new City(CityKind.Locality, $"Praia{tag}", $"Praia{tag}", null, $"praia{tag}", "XX", state, $"praia{tag}-vila{tag}-{state.ToLowerInvariant()}", "test", null, null, town.Id, -20.01, -40.01, "America/Sao_Paulo", 0);
            db.Cities.Add(village);
            await db.SaveChangesAsync();
            return (town, village);
        });
    }

    private static async Task<LocationResponse> PostLocationAsync(HttpClient client, Guid cityId, double lng) =>
        (await (await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest($"Local {Tag()}", -20, lng, CityId: cityId, ConfirmDistinct: true)))
            .Content.ReadFromJsonAsync<LocationResponse>())!;

    [Fact]
    public async Task ALocalityPage_ExistsOnceALocationHasAPhoto_AndBringsOnlyItsOwnContent()
    {
        var (town, village) = await SeedAsync(NewState());
        var (_, user) = await RegisterAndAuthenticateAsync();
        var inVillage = await PostLocationAsync(user, village.Id, -40.0);
        var inTownOnly = await PostLocationAsync(user, town.Id, -39.0);
        var photo = await CreatePhotoAsync(user, inVillage.Id);
        await CreatePhotoAsync(user, inTownOnly.Id);

        var page = (await (await GetAsync($"/api/v1/cities/{village.Slug}")).Content.ReadFromJsonAsync<CityDetailResponse>())!;
        var locations = (await (await GetAsync($"/api/v1/cities/{village.Slug}/locations")).Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>())!;
        var photos = (await (await GetAsync($"/api/v1/cities/{village.Slug}/photos")).Content.ReadFromJsonAsync<CursorPagedResult<PhotoResponse>>())!;
        var townPage = (await (await GetAsync($"/api/v1/cities/{town.Slug}")).Content.ReadFromJsonAsync<CityDetailResponse>())!;

        Assert.Equal(CityKind.Locality, page.Kind);
        Assert.Equal(town.Slug, page.Parent!.Slug);
        Assert.Equal((1, 1), (page.LocationsCount, page.PhotosCount));
        Assert.Equal(inVillage.Id, page.TopLocation!.Id);
        Assert.Empty(page.Localities);
        Assert.Equal([inVillage.Id], locations.Items.Select(l => l.Id));       // not the municipality's other location
        Assert.Equal([photo.Id], photos.Items.Select(p => p.Id));
        Assert.Equal((2, 2), (townPage.LocationsCount, townPage.PhotosCount)); // the municipality still counts both
        Assert.Null(townPage.Parent);
    }

    [Fact]
    public async Task ALocalityWithLocationsButNoPhoto_IsStillA404()
    {
        var (_, village) = await SeedAsync(NewState());
        var (_, user) = await RegisterAndAuthenticateAsync();
        await PostLocationAsync(user, village.Id, -40.0);

        foreach (var path in new[] { "", "/locations", "/photos" })
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/v1/cities/{village.Slug}{path}")).StatusCode);
    }

    [Fact]
    public async Task TheLocalityList_HoldsOnlyLocalitiesWithAPhotoedLocation_UnlessTheWholeCatalogIsAsked()
    {
        var state = NewState();
        var (_, withPhoto) = await SeedAsync(state);
        var (_, withoutPhoto) = await SeedAsync(state);
        var (_, user) = await RegisterAndAuthenticateAsync();
        var shown = await PostLocationAsync(user, withPhoto.Id, -40.0);
        await PostLocationAsync(user, withoutPhoto.Id, -39.0);
        await CreatePhotoAsync(user, shown.Id);

        foreach (var sort in new[] { "population", "locations", "rating", "name" })
        {
            var list = (await (await GetAsync($"/api/v1/cities?kind=locality&state={state}&sort={sort}")).Content.ReadFromJsonAsync<CursorPagedResult<CityResponse>>())!;
            Assert.Equal([withPhoto.Slug], list.Items.Select(c => c.Slug));
        }

        var whole = (await (await GetAsync($"/api/v1/cities?kind=locality&state={state}&includeEmpty=true&sort=name")).Content.ReadFromJsonAsync<CursorPagedResult<CityResponse>>())!;
        Assert.Equal(2, whole.Items.Count);
    }

    [Fact]
    public async Task TheSitemap_ListsLocalitiesWithAPhotoedLocation_AndCitiesStillComeSeparately()
    {
        var state = NewState();
        var (town, withPhoto) = await SeedAsync(state);
        var (_, withoutPhoto) = await SeedAsync(state);
        var (_, user) = await RegisterAndAuthenticateAsync();
        var shown = await PostLocationAsync(user, withPhoto.Id, -40.0);
        await PostLocationAsync(user, withoutPhoto.Id, -39.0);
        await CreatePhotoAsync(user, shown.Id);

        var localities = await WalkAsync("locality");
        var cities = await WalkAsync("city");

        Assert.Contains(localities, e => e.Id == withPhoto.Id && e.Slug == withPhoto.Slug);
        Assert.DoesNotContain(localities, e => e.Id == withoutPhoto.Id);
        Assert.DoesNotContain(localities, e => e.Id == town.Id);               // municipalities are the "city" type
        Assert.Contains(cities, e => e.Id == town.Id);
        Assert.DoesNotContain(cities, e => e.Id == withPhoto.Id);
    }

    private async Task<List<SitemapEntryResponse>> WalkAsync(string type)
    {
        var all = new List<SitemapEntryResponse>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/sitemap/entries?type={type}&limit=50" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await CreateClient().GetFromJsonAsync<CursorPagedResult<SitemapEntryResponse>>(url))!;
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return all;
    }
}

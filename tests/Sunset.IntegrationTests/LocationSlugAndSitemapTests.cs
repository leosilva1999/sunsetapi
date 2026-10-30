using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Sitemap;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationSlugAndSitemapTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<City> NewCityAsync(string tag) =>
        await WithDbAsync(async db =>
        {
            var city = new City(CityKind.Municipality, $"Slugvila{tag}", $"Slugvila{tag}", null, $"slugvila{tag}", "XX", "ZS", $"slugvila{tag}-zs", "test", null, null, null, -20, -40, "America/Sao_Paulo", 0);
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            return city;
        });

    private static async Task<LocationResponse> CreateAsync(HttpClient client, string name, Guid cityId) =>
        (await (await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, -20, -40, CityId: cityId)))
            .Content.ReadFromJsonAsync<LocationResponse>())!;

    [Fact]
    public async Task ALocation_GetsANameAndCitySlug_AndSameNamesGetANumericSuffix()
    {
        var tag = Tag();
        var city = await NewCityAsync(tag);
        var (_, client) = await RegisterAndAuthenticateAsync();

        var first = await CreateAsync(client, "Praia do Rosa", city.Id);
        var second = await CreateAsync(client, "Praia do Rosa", city.Id);

        Assert.Equal($"praia-do-rosa-{city.Slug}", first.Slug);
        Assert.Equal($"praia-do-rosa-{city.Slug}-2", second.Slug);
    }

    [Fact]
    public async Task TheDetail_AnswersToTheIdAndToTheSlug_AndAnUnknownSlugIs404()
    {
        var city = await NewCityAsync(Tag());
        var (_, client) = await RegisterAndAuthenticateAsync();
        var created = await CreateAsync(client, "Mirante Azul", city.Id);

        var byId = await CreateClient().GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{created.Id}");
        var bySlug = await CreateClient().GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{created.Slug}");

        Assert.Equal(created.Id, byId!.Id);
        Assert.Equal(created.Id, bySlug!.Id);
        Assert.Equal(created.Slug, bySlug.Slug);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateClient().GetAsync("/api/v1/locations/nao-existe-mesmo")).StatusCode);
    }

    [Fact]
    public async Task LiteralRoutes_StillWinOverTheSlugRoute()
    {
        var ranking = await CreateClient().GetAsync("/api/v1/locations/ranking");

        Assert.Equal(HttpStatusCode.OK, ranking.StatusCode);
    }

    [Fact]
    public async Task ARemovedLocation_KeepsItsSlugReserved_AndItsPageIs404()
    {
        var city = await NewCityAsync(Tag());
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var first = await CreateAsync(client, "Gruta", city.Id);
        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{first.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await CreateClient().GetAsync($"/api/v1/locations/{first.Slug}")).StatusCode);
        var again = await CreateAsync(client, "Gruta", city.Id);

        Assert.Equal($"{first.Slug}-2", again.Slug);
    }

    private async Task<List<SitemapEntryResponse>> WalkAsync(string type)
    {
        var all = new List<SitemapEntryResponse>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/sitemap/entries?type={type}&limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await CreateClient().GetFromJsonAsync<CursorPagedResult<SitemapEntryResponse>>(url))!;
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return all;
    }

    [Fact]
    public async Task TheSitemap_ListsOnlyVisibleLocationsAndCitiesWithOne_PagedWithoutRepeats()
    {
        var city = await NewCityAsync(Tag());
        var emptyCity = await NewCityAsync(Tag());
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var kept = await CreateAsync(client, "Visivel", city.Id);
        var hidden = await CreateAsync(client, "Escondido", city.Id);
        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{hidden.Id}")).EnsureSuccessStatusCode();

        var locations = await WalkAsync("location");
        var cities = await WalkAsync("city");

        Assert.Contains(locations, e => e.Id == kept.Id && e.Slug == kept.Slug);
        Assert.DoesNotContain(locations, e => e.Id == hidden.Id);
        Assert.Equal(locations.Count, locations.Select(e => e.Id).Distinct().Count());
        Assert.Contains(cities, e => e.Id == city.Id && e.Slug == city.Slug);
        Assert.DoesNotContain(cities, e => e.Id == emptyCity.Id);
    }

    [Fact]
    public async Task TheSitemap_LastModifiedFollowsTheLatestPhoto_AndPhotosAreListedWithoutSlug()
    {
        var city = await NewCityAsync(Tag());
        var (_, client) = await RegisterAndAuthenticateAsync();
        var created = await CreateAsync(client, "Com Foto", city.Id);
        var before = (await WalkAsync("location")).Single(e => e.Id == created.Id).LastModified;
        await Task.Delay(50);

        var photo = await CreatePhotoAsync(client, created.Id);

        var after = (await WalkAsync("location")).Single(e => e.Id == created.Id).LastModified;
        var photos = await WalkAsync("photo");
        Assert.True(after > before);
        Assert.Contains(photos, e => e.Slug is null);
        Assert.Contains(photos, e => e.Id == photo.Id);
    }

    [Fact]
    public async Task TheSitemap_RejectsAnUnknownType_AndIsNotSpentFromTheSearchQuota()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateClient().GetAsync("/api/v1/sitemap/entries?type=99")).StatusCode);

        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await CreateClient().GetAsync("/api/v1/sitemap/entries?type=city&limit=1")).StatusCode);
    }
}

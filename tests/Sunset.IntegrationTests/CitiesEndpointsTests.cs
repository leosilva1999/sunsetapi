using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class CitiesEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    // The suite shares one database: every test seeds cities under its own random tag and filters by it.
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // GET /cities shares the location-search rate limit, which is keyed by client IP; the whole suite
    // otherwise looks like ONE client. A private address per test keeps these tests off that bucket.
    private readonly string _clientIp = $"198.18.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    private Task<HttpResponseMessage> GetAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", _clientIp);
        return CreateClient().SendAsync(request);
    }

    private Task<City> SeedAsync(
        string name, string state, int population = 0, string? alternateNames = null, bool active = true,
        CityKind kind = CityKind.Municipality, Guid? parentId = null, string? slug = null) =>
        WithDbAsync(async db =>
        {
            var normalized = CityImportPlannerSearchText(name, alternateNames);
            var city = new City(kind, name, name, alternateNames, normalized, "XX", state, slug ?? $"{Sunset.Application.Common.SlugGenerator.Slugify(name)}-{Tag()}",
                "test", null, null, parentId, -20, -40, "America/Sao_Paulo", population);
            if (!active)
                city.Deactivate();
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            return city;
        });

    private static string CityImportPlannerSearchText(string name, string? alt) =>
        Sunset.Application.Cities.Import.CityImportPlanner.BuildSearchText(name, name, alt);

    private async Task<CursorPagedResult<CityResponse>> ListAsync(string query)
    {
        var response = await GetAsync($"/api/v1/cities?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CursorPagedResult<CityResponse>>())!;
    }

    [Fact]
    public async Task Search_IgnoresAccentsAndCase_AndMatchesPrefixes()
    {
        var tag = Tag();
        await SeedAsync($"Ítaca{tag}", "T1");

        var byPrefix = await ListAsync($"q=itaca{tag[..5]}");
        var byFullName = await ListAsync($"q=ITACA{tag}");

        Assert.Contains(byPrefix.Items, c => c.Name == $"Ítaca{tag}");
        Assert.Contains(byFullName.Items, c => c.Name == $"Ítaca{tag}");
    }

    [Fact]
    public async Task Search_AlsoMatchesAlternateNames()
    {
        var tag = Tag();
        await SeedAsync($"Goiasminas{tag}", "T2", alternateNames: $"Goiaminas{tag},Velha{tag}");

        var page = await ListAsync($"q=goiaminas{tag}");

        Assert.Contains(page.Items, c => c.Name == $"Goiasminas{tag}");
    }

    [Fact]
    public async Task Search_RequiresAllTheWords()
    {
        var tag = Tag();
        await SeedAsync($"Santa{tag} Helena", "T3");

        Assert.Contains((await ListAsync($"q=santa{tag}+hel")).Items, c => c.Name == $"Santa{tag} Helena");
        Assert.Empty((await ListAsync($"q=santa{tag}+zzz")).Items);
    }

    [Fact]
    public async Task KindFilter_SeparatesMunicipalitiesFromLocalities_AndLocalitiesShowTheirParent()
    {
        var tag = Tag();
        var city = await SeedAsync($"Zeta{tag}", "T4", slug: $"zeta-{tag}-t4");
        await SeedAsync($"Zeta{tag} Praia", "T4", kind: CityKind.Locality, parentId: city.Id, slug: $"zeta-{tag}-praia-t4");

        var municipalities = await ListAsync($"q=zeta{tag}");
        var localities = await ListAsync($"q=zeta{tag}&kind=locality");
        var any = await ListAsync($"q=zeta{tag}&kind=any");

        Assert.Equal([$"Zeta{tag}"], municipalities.Items.Select(c => c.Name));
        var locality = Assert.Single(localities.Items);
        Assert.Equal(CityKind.Locality, locality.Kind);
        Assert.Equal($"zeta-{tag}-t4", locality.Parent!.Slug);
        Assert.Equal("T4", locality.Parent.State);
        Assert.Equal(2, any.Items.Count);
    }

    [Fact]
    public async Task StateFilter_IsCaseInsensitive_AndPopulationIsTheDefaultOrder_WithCursorPaging()
    {
        var tag = Tag();
        var state = $"S{tag[..6]}".ToUpperInvariant();   // real states are upper case, and the filter compares upper case
        foreach (var (name, population) in new[] { ("Pequena", 10), ("Grande", 900), ("Media", 300), ("Minima", 1), ("Enorme", 5000) })
            await SeedAsync($"{name}{tag}", state, population);

        var first = await ListAsync($"state={state.ToLowerInvariant()}&limit=2");
        var second = await ListAsync($"state={state}&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        var third = await ListAsync($"state={state}&limit=2&cursor={Uri.EscapeDataString(second.NextCursor!)}");

        var all = first.Items.Concat(second.Items).Concat(third.Items).ToList();
        Assert.Equal(
            [$"Enorme{tag}", $"Grande{tag}", $"Media{tag}", $"Pequena{tag}", $"Minima{tag}"],
            all.Select(c => c.Name));
        Assert.True(first.HasMore && second.HasMore);
        Assert.False(third.HasMore);
        Assert.Null(third.NextCursor);
        Assert.All(all, c => Assert.Equal(state, c.State));
    }

    [Fact]
    public async Task SortByName_OrdersAlphabetically_WithCursorPaging()
    {
        var tag = Tag();
        var state = $"N{tag[..6]}".ToUpperInvariant();
        foreach (var name in new[] { "Charlie", "Alfa", "Echo", "Bravo", "Delta" })
            await SeedAsync($"{name}{tag}", state);

        var first = await ListAsync($"state={state}&sort=name&limit=3");
        var second = await ListAsync($"state={state}&sort=name&limit=3&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        Assert.Equal(
            [$"Alfa{tag}", $"Bravo{tag}", $"Charlie{tag}", $"Delta{tag}", $"Echo{tag}"],
            first.Items.Concat(second.Items).Select(c => c.Name));
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task InactiveCities_AreNotListed_AndHaveNoPage()
    {
        var tag = Tag();
        var gone = await SeedAsync($"Sumida{tag}", "T5", active: false);

        Assert.Empty((await ListAsync($"q=sumida{tag}")).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/v1/cities/{gone.Slug}")).StatusCode);
    }

    [Fact]
    public async Task GetBySlug_ReturnsTheMunicipality_WhateverTheCaseOfTheSlug()
    {
        var tag = Tag();
        var city = await SeedAsync($"Visivel{tag}", "T6", population: 1234);

        var response = await GetAsync($"/api/v1/cities/{city.Slug.ToUpperInvariant()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CityResponse>())!;
        Assert.Equal(city.Slug, body.Slug);
        Assert.Equal($"Visivel{tag}", body.Name);
        Assert.Equal(1234, body.Population);
        Assert.Equal("America/Sao_Paulo", body.TimeZoneId);
        Assert.Equal("XX", body.Country);
        Assert.Null(body.Parent);
    }

    [Fact]
    public async Task GetBySlug_ForALocality_OrAnUnknownSlug_IsNotFound()
    {
        var tag = Tag();
        var city = await SeedAsync($"Pai{tag}", "T7");
        var locality = await SeedAsync($"Filho{tag}", "T7", kind: CityKind.Locality, parentId: city.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/v1/cities/{locality.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("/api/v1/cities/nao-existe-xx")).StatusCode);
    }

    [Theory]
    [InlineData("q=a")]
    [InlineData("country=BRA")]
    [InlineData("kind=planet")]
    [InlineData("sort=colour")]
    public async Task InvalidParameters_ReturnBadRequest(string query)
    {
        var response = await GetAsync($"/api/v1/cities?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShortSearchTerm_ReportsTheQField()
    {
        var response = await GetAsync("/api/v1/cities?q=a");

        Assert.Contains("\"Q\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListIsPublic_AndDefaultsToMunicipalities()
    {
        var tag = Tag();
        var city = await SeedAsync($"Publica{tag}", "T8");
        await SeedAsync($"Publica{tag} Vila", "T8", kind: CityKind.Locality, parentId: city.Id);

        var page = await ListAsync($"q=publica{tag}");

        Assert.Single(page.Items);
        Assert.Equal(CityKind.Municipality, page.Items[0].Kind);
    }
}

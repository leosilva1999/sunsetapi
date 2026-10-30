using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
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

    private Task<HttpResponseMessage> GetAsync(string url, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", _clientIp);
        return (client ?? CreateClient()).SendAsync(request);
    }

    private Task<City> SeedAsync(
        string name, string state, int population = 0, string? alternateNames = null, bool active = true,
        CityKind kind = CityKind.Municipality, Guid? parentId = null, string? slug = null) =>
        WithDbAsync(async db =>
        {
            var searchText = Sunset.Application.Cities.Import.CityImportPlanner.BuildSearchText(name, name, alternateNames);
            var city = new City(kind, name, name, alternateNames, searchText, "XX", state, slug ?? $"{SlugGenerator.Slugify(name)}-{Tag()}",
                "test", null, null, parentId, -20, -40, "America/Sao_Paulo", population);
            if (!active)
                city.Deactivate();
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            return city;
        });

    private async Task<CursorPagedResult<CityResponse>> ListAsync(string query)
    {
        var response = await GetAsync($"/api/v1/cities?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CursorPagedResult<CityResponse>>())!;
    }

    // The whole catalog, by population: what the registration autocomplete asks for.
    private Task<CursorPagedResult<CityResponse>> CatalogAsync(string query, string sort = "population") =>
        ListAsync($"{query}&includeEmpty=true&sort={sort}");

    private static async Task<LocationResponse> PostLocationAsync(HttpClient client, Guid cityId, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(name ?? $"Local {Tag()}", -20, -40, CityId: cityId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationResponse>())!;
    }

    private async Task RateAsync(Guid locationId, int score)
    {
        var (_, rater) = await RegisterAndAuthenticateAsync();
        (await rater.PostAsJsonAsync($"/api/v1/locations/{locationId}/ratings", new CreateRatingRequest(score))).EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------- search of the catalog

    [Fact]
    public async Task Search_IgnoresAccentsAndCase_AndMatchesPrefixes()
    {
        var tag = Tag();
        await SeedAsync($"Ítaca{tag}", "T1");

        var byPrefix = await CatalogAsync($"q=itaca{tag[..5]}");
        var byFullName = await CatalogAsync($"q=ITACA{tag}");

        Assert.Contains(byPrefix.Items, c => c.Name == $"Ítaca{tag}");
        Assert.Contains(byFullName.Items, c => c.Name == $"Ítaca{tag}");
    }

    [Fact]
    public async Task Search_AlsoMatchesAlternateNames()
    {
        var tag = Tag();
        await SeedAsync($"Goiasminas{tag}", "T2", alternateNames: $"Goiaminas{tag},Velha{tag}");

        var page = await CatalogAsync($"q=goiaminas{tag}");

        Assert.Contains(page.Items, c => c.Name == $"Goiasminas{tag}");
    }

    [Fact]
    public async Task Search_RequiresAllTheWords()
    {
        var tag = Tag();
        await SeedAsync($"Santa{tag} Helena", "T3");

        Assert.Contains((await CatalogAsync($"q=santa{tag}+hel")).Items, c => c.Name == $"Santa{tag} Helena");
        Assert.Empty((await CatalogAsync($"q=santa{tag}+zzz")).Items);
    }

    [Fact]
    public async Task KindFilter_SeparatesMunicipalitiesFromLocalities_AndLocalitiesShowTheirParent()
    {
        var tag = Tag();
        var city = await SeedAsync($"Zeta{tag}", "T4", slug: $"zeta-{tag}-t4");
        await SeedAsync($"Zeta{tag} Praia", "T4", kind: CityKind.Locality, parentId: city.Id, slug: $"zeta-{tag}-praia-t4");

        var municipalities = await CatalogAsync($"q=zeta{tag}");
        var localities = await CatalogAsync($"q=zeta{tag}&kind=locality");
        var any = await CatalogAsync($"q=zeta{tag}&kind=any");

        Assert.Equal([$"Zeta{tag}"], municipalities.Items.Select(c => c.Name));
        var locality = Assert.Single(localities.Items);
        Assert.Equal(CityKind.Locality, locality.Kind);
        Assert.Equal($"zeta-{tag}-t4", locality.Parent!.Slug);
        Assert.Equal("T4", locality.Parent.State);
        Assert.Equal(2, any.Items.Count);
    }

    [Fact]
    public async Task StateFilter_IsCaseInsensitive_AndPopulationOrdersWithCursorPaging()
    {
        var tag = Tag();
        var state = $"S{tag[..6]}".ToUpperInvariant();   // real states are upper case, and the filter compares upper case
        foreach (var (name, population) in new[] { ("Pequena", 10), ("Grande", 900), ("Media", 300), ("Minima", 1), ("Enorme", 5000) })
            await SeedAsync($"{name}{tag}", state, population);

        var first = await CatalogAsync($"state={state.ToLowerInvariant()}&limit=2");
        var second = await CatalogAsync($"state={state}&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        var third = await CatalogAsync($"state={state}&limit=2&cursor={Uri.EscapeDataString(second.NextCursor!)}");

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

        var first = await CatalogAsync($"state={state}&limit=3", "name");
        var second = await CatalogAsync($"state={state}&limit=3&cursor={Uri.EscapeDataString(first.NextCursor!)}", "name");

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

        Assert.Empty((await CatalogAsync($"q=sumida{tag}")).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/v1/cities/{gone.Slug}")).StatusCode);
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

    // ---------------------------------------------------------------- cities that have locations

    [Fact]
    public async Task TheDefaultList_OnlyHasCitiesThatHaveLocations()
    {
        var tag = Tag();
        var state = $"D{tag[..6]}".ToUpperInvariant();
        var withLocation = await SeedAsync($"Ativa{tag}", state);
        await SeedAsync($"Vazia{tag}", state);
        var (_, user) = await RegisterAndAuthenticateAsync();
        await PostLocationAsync(user, withLocation.Id);

        var defaultList = await ListAsync($"state={state}");
        var everything = await CatalogAsync($"state={state}");

        Assert.Equal([$"Ativa{tag}"], defaultList.Items.Select(c => c.Name));
        Assert.Equal(2, everything.Items.Count);
    }

    [Fact]
    public async Task ACitysCounts_AndCover_ComeFromItsLocations()
    {
        var tag = Tag();
        var state = $"C{tag[..6]}".ToUpperInvariant();
        var city = await SeedAsync($"Contada{tag}", state);
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var first = await PostLocationAsync(owner, city.Id);
        var second = await PostLocationAsync(owner, city.Id);

        var quiet = await CreatePhotoAsync(owner, first.Id);
        var liked = await CreatePhotoAsync(owner, second.Id);
        var (_, fan) = await RegisterAndAuthenticateAsync();
        (await fan.PostAsync($"/api/v1/photos/{liked.Id}/likes", null)).EnsureSuccessStatusCode();
        await RateAsync(first.Id, 5);
        await RateAsync(second.Id, 3);

        var item = Assert.Single((await ListAsync($"state={state}")).Items);

        Assert.Equal(2, item.LocationsCount);
        Assert.Equal(2, item.PhotosCount);
        Assert.Equal(2, item.RatingsCount);
        Assert.Equal(4.00m, item.AvgRating);        // average of ALL the ratings
        Assert.Equal(liked.ImageUrl, item.CoverImageUrl);   // the most liked photo
        Assert.NotEqual(quiet.ImageUrl, item.CoverImageUrl);
    }

    [Fact]
    public async Task SortByLocations_AndByRating_OrderTheCitiesThatHaveLocations_WithCursorPaging()
    {
        var tag = Tag();
        var state = $"R{tag[..6]}".ToUpperInvariant();
        var many = await SeedAsync($"Muitos{tag}", state);       // 3 locations, rated 2
        var best = await SeedAsync($"Melhor{tag}", state);       // 1 location, rated 5
        var plain = await SeedAsync($"Sem{tag}", state);         // 2 locations, no ratings
        await SeedAsync($"Nenhum{tag}", state);                  // no locations: never ranked
        var (_, user) = await RegisterAndAuthenticateAsync();
        var manyLocations = new[] { await PostLocationAsync(user, many.Id), await PostLocationAsync(user, many.Id), await PostLocationAsync(user, many.Id) };
        var bestLocation = await PostLocationAsync(user, best.Id);
        await PostLocationAsync(user, plain.Id);
        await PostLocationAsync(user, plain.Id);
        await RateAsync(manyLocations[0].Id, 2);
        await RateAsync(bestLocation.Id, 5);

        async Task<List<string>> Walk(string sort)
        {
            var names = new List<string>();
            string? cursor = null;
            do
            {
                var page = await ListAsync($"state={state}&sort={sort}&limit=1" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
                names.AddRange(page.Items.Select(c => c.Name));
                cursor = page.NextCursor;
            } while (cursor is not null);
            return names;
        }

        Assert.Equal([$"Muitos{tag}", $"Sem{tag}", $"Melhor{tag}"], await Walk("locations"));
        Assert.Equal([$"Melhor{tag}", $"Muitos{tag}", $"Sem{tag}"], await Walk("rating"));
    }

    [Fact]
    public async Task ALocalitysCounts_OnlyIncludeTheLocationsTiedToIt()
    {
        var tag = Tag();
        var state = $"L{tag[..6]}".ToUpperInvariant();
        var city = await SeedAsync($"Cidade{tag}", state, slug: $"cidade-{tag}-l");
        var locality = await SeedAsync($"Praia{tag}", state, kind: CityKind.Locality, parentId: city.Id, slug: $"praia-{tag}-l");
        var (_, user) = await RegisterAndAuthenticateAsync();
        await PostLocationAsync(user, locality.Id);   // tied to the locality (and through it, to the city)
        await PostLocationAsync(user, city.Id);       // tied to the city only

        var cityItem = Assert.Single((await ListAsync($"state={state}")).Items);
        var localityItem = Assert.Single((await ListAsync($"state={state}&kind=locality")).Items);

        Assert.Equal(2, cityItem.LocationsCount);       // its own + the locality's
        Assert.Equal(1, localityItem.LocationsCount);   // only those tied to the locality
    }

    // ---------------------------------------------------------------- the city page

    [Fact]
    public async Task TheCityPage_BringsCountsTheBestLocationAndTheLocalities()
    {
        var tag = Tag();
        var city = await SeedAsync($"Pagina{tag}", "T6", population: 1234, slug: $"pagina-{tag}-t6");
        var locality = await SeedAsync($"Vila{tag}", "T6", kind: CityKind.Locality, parentId: city.Id, slug: $"vila-{tag}-t6");
        var (_, user) = await RegisterAndAuthenticateAsync();
        var plain = await PostLocationAsync(user, city.Id, $"Comum {tag}");
        var star = await PostLocationAsync(user, locality.Id, $"Estrela {tag}");
        await RateAsync(star.Id, 5);
        await RateAsync(plain.Id, 3);
        await CreatePhotoAsync(user, star.Id);

        var response = await GetAsync($"/api/v1/cities/{city.Slug.ToUpperInvariant()}");   // the slug is case-insensitive

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CityDetailResponse>())!;
        Assert.Equal($"Pagina{tag}", body.Name);
        Assert.Equal(city.Slug, body.Slug);
        Assert.Equal(1234, body.Population);
        Assert.Equal("America/Sao_Paulo", body.TimeZoneId);
        Assert.Equal((2, 1, 2, 4.00m), (body.LocationsCount, body.PhotosCount, body.RatingsCount, body.AvgRating));
        Assert.Equal($"Estrela {tag}", body.TopLocation!.Name);
        Assert.Equal(5.00m, body.TopLocation.AvgRating);
        var listed = Assert.Single(body.Localities);
        Assert.Equal((locality.Slug, 1), (listed.Slug, listed.LocationsCount));
        Assert.NotNull(body.CoverImageUrl);
    }

    [Fact]
    public async Task ACityWithoutLocations_HasNoPage_OnAnyOfItsEndpoints()
    {
        var tag = Tag();
        var city = await SeedAsync($"Vazia{tag}", "T7", slug: $"vazia-{tag}-t7");

        foreach (var path in new[] { "", "/locations", "/photos", "/sunset" })
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/v1/cities/{city.Slug}{path}")).StatusCode);
    }

    [Fact]
    public async Task ALocality_OrAnUnknownSlug_IsNotFound()
    {
        var tag = Tag();
        var city = await SeedAsync($"Pai{tag}", "T8");
        var locality = await SeedAsync($"Filho{tag}", "T8", kind: CityKind.Locality, parentId: city.Id);
        var (_, user) = await RegisterAndAuthenticateAsync();
        await PostLocationAsync(user, locality.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/v1/cities/{locality.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("/api/v1/cities/nao-existe-xx")).StatusCode);
    }

    [Fact]
    public async Task TheCitysLocations_ArePagedAndOrdered_AndLeaveOutOtherCitiesAndDeletedOnes()
    {
        var tag = Tag();
        var city = await SeedAsync($"Locais{tag}", "T9", slug: $"locais-{tag}-t9");
        var other = await SeedAsync($"Outra{tag}", "T9");
        var (_, user) = await RegisterAndAuthenticateAsync();
        var alfa = await PostLocationAsync(user, city.Id, $"Alfa {tag}");
        var bravo = await PostLocationAsync(user, city.Id, $"Bravo {tag}");
        var charlie = await PostLocationAsync(user, city.Id, $"Charlie {tag}");
        var removed = await PostLocationAsync(user, city.Id, $"Removido {tag}");
        await PostLocationAsync(user, other.Id, $"De outra {tag}");
        await RateAsync(bravo.Id, 5);
        await RateAsync(alfa.Id, 4);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{removed.Id}")).EnsureSuccessStatusCode();

        async Task<List<LocationResponse>> Walk(string sort)
        {
            var items = new List<LocationResponse>();
            string? cursor = null;
            do
            {
                var response = await GetAsync($"/api/v1/cities/{city.Slug}/locations?sort={sort}&limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
                response.EnsureSuccessStatusCode();
                var page = (await response.Content.ReadFromJsonAsync<CursorPagedResult<LocationResponse>>())!;
                items.AddRange(page.Items);
                cursor = page.NextCursor;
            } while (cursor is not null);
            return items;
        }

        var byRating = await Walk("rating");
        Assert.Equal([bravo.Id, alfa.Id], byRating.Take(2).Select(l => l.Id));   // 5, then 4; the unrated follow
        Assert.Equal(3, byRating.Count);
        Assert.Equal([alfa.Id, bravo.Id, charlie.Id], (await Walk("name")).Select(l => l.Id));
        Assert.Equal([charlie.Id, bravo.Id, alfa.Id], (await Walk("recent")).Select(l => l.Id));
        Assert.All(byRating, l => Assert.Equal(city.Slug, l.CitySlug));
        Assert.Equal(1, byRating.Single(l => l.Id == bravo.Id).RatingsCount);
    }

    [Fact]
    public async Task TheCitysPhotos_JoinAllItsLocations_AndKnowWhatTheVisitorLiked()
    {
        var tag = Tag();
        var city = await SeedAsync($"Fotos{tag}", "TA", slug: $"fotos-{tag}-ta");
        var locality = await SeedAsync($"Praia{tag}", "TA", kind: CityKind.Locality, parentId: city.Id);
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var inCity = await CreatePhotoAsync(owner, (await PostLocationAsync(owner, city.Id)).Id);
        var inLocality = await CreatePhotoAsync(owner, (await PostLocationAsync(owner, locality.Id)).Id);
        var hiddenLocation = await PostLocationAsync(owner, city.Id);
        await CreatePhotoAsync(owner, hiddenLocation.Id);
        var (_, fan) = await RegisterAndAuthenticateAsync();
        (await fan.PostAsync($"/api/v1/photos/{inLocality.Id}/likes", null)).EnsureSuccessStatusCode();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{hiddenLocation.Id}")).EnsureSuccessStatusCode();

        var anonymous = (await (await GetAsync($"/api/v1/cities/{city.Slug}/photos")).Content.ReadFromJsonAsync<CursorPagedResult<PhotoResponse>>())!;
        var asFan = (await (await GetAsync($"/api/v1/cities/{city.Slug}/photos", fan)).Content.ReadFromJsonAsync<CursorPagedResult<PhotoResponse>>())!;

        Assert.Equal([inLocality.Id, inCity.Id], anonymous.Items.Select(p => p.Id));   // top: the liked one first
        Assert.All(anonymous.Items, p => Assert.False(p.LikedByCurrentUser));
        Assert.True(asFan.Items.Single(p => p.Id == inLocality.Id).LikedByCurrentUser);   // correct with a token
        Assert.False(asFan.Items.Single(p => p.Id == inCity.Id).LikedByCurrentUser);

        var recent = (await (await GetAsync($"/api/v1/cities/{city.Slug}/photos?sort=recent")).Content.ReadFromJsonAsync<CursorPagedResult<PhotoResponse>>())!;
        Assert.Equal([inLocality.Id, inCity.Id], recent.Items.Select(p => p.Id));   // newest first
    }
}

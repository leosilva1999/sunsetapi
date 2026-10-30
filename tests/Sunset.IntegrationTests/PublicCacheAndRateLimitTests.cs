using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class PublicCacheAndRateLimitTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // Each test works from a client address of its own (the suite otherwise looks like one client to the limiter).
    private readonly string _clientIp = $"198.19.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    private Task<HttpResponseMessage> GetAsync(string url, bool cache = false, HttpClient? client = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", _clientIp);
        if (cache)
            request.Headers.Add("X-Test-Cache", "on");   // the cache is opt-in in the test environment
        configure?.Invoke(request);
        return (client ?? CreateClient()).SendAsync(request);
    }

    private async Task<(LocationResponse Location, string Name)> NewLocationAsync(HttpClient client)
    {
        var name = $"Cache {Tag()}";
        var created = (await (await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, -20, -40, "Florianopolis")))
            .Content.ReadFromJsonAsync<LocationResponse>())!;
        return (created, name);
    }

    // Changes a row behind the API's back, so the API's cache is the only thing that can still show the old value.
    private Task RenameBehindTheApiAsync(Guid locationId, string newName) =>
        WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE locations SET \"Name\" = {newName} WHERE \"Id\" = {locationId}");
            return 0;
        });

    // ---------------------------------------------------------------- the response cache

    [Fact]
    public async Task APublicRead_IsServedFromTheCache_UntilSomethingEvictsIt()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (location, name) = await NewLocationAsync(owner);
        var url = $"/api/v1/locations/{location.Id}";

        var first = (await (await GetAsync(url, cache: true)).Content.ReadFromJsonAsync<LocationResponse>())!;
        Assert.Equal(name, first.Name);

        await RenameBehindTheApiAsync(location.Id, "Mudado por fora");
        var cached = (await (await GetAsync(url, cache: true)).Content.ReadFromJsonAsync<LocationResponse>())!;
        Assert.Equal(name, cached.Name);   // still the cached copy: the database was not asked

        // a moderator's edit goes through the API, which evicts the cache
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        (await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/coordinates", new Sunset.Application.DTOs.Moderation.UpdateLocationCoordinatesRequest(-21, -41)))
            .EnsureSuccessStatusCode();

        var fresh = (await (await GetAsync(url, cache: true)).Content.ReadFromJsonAsync<LocationResponse>())!;
        Assert.Equal("Mudado por fora", fresh.Name);
        Assert.Equal(-21, fresh.Latitude);
    }

    [Fact]
    public async Task ARequestWithAToken_IsNeverServedFromNorStoredInTheCache()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (location, name) = await NewLocationAsync(owner);
        var url = $"/api/v1/locations/{location.Id}";

        await GetAsync(url, cache: true);                       // anonymous: cached
        await RenameBehindTheApiAsync(location.Id, "Mudado por fora");

        var signedIn = (await (await GetAsync(url, cache: true, client: owner)).Content.ReadFromJsonAsync<LocationResponse>())!;
        var anonymous = (await (await GetAsync(url, cache: true)).Content.ReadFromJsonAsync<LocationResponse>())!;

        Assert.Equal("Mudado por fora", signedIn.Name);   // went to the database
        Assert.Equal(name, anonymous.Name);               // and did not overwrite what anonymous visitors get
    }

    [Fact]
    public async Task WithoutTheTestOptIn_NothingIsCached()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (location, _) = await NewLocationAsync(owner);
        var url = $"/api/v1/locations/{location.Id}";

        await GetAsync(url);
        await RenameBehindTheApiAsync(location.Id, "Mudado por fora");

        Assert.Equal("Mudado por fora", (await (await GetAsync(url)).Content.ReadFromJsonAsync<LocationResponse>())!.Name);
    }

    [Fact]
    public async Task APublicRead_CarriesCacheControlAndAnETag_AndAnswers304ToAMatchingOne()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (location, _) = await NewLocationAsync(owner);
        var url = $"/api/v1/locations/{location.Id}";

        var first = await GetAsync(url, cache: true);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("public, max-age=60, stale-while-revalidate=300", first.Headers.CacheControl?.ToString());
        var etag = first.Headers.ETag!.ToString();
        Assert.StartsWith("W/\"", etag);

        // on a cache hit (and on a miss) a matching If-None-Match gets an empty 304
        var again = await GetAsync(url, cache: true, configure: r => r.Headers.TryAddWithoutValidation("If-None-Match", etag));
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Empty(await again.Content.ReadAsByteArrayAsync());

        var other = await GetAsync(url, cache: true, configure: r => r.Headers.TryAddWithoutValidation("If-None-Match", "W/\"outro\""));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task TheCacheKeepsOneEntryPerQueryAndPerOrigin()
    {
        var tag = Tag();
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (location, _) = await NewLocationAsync(owner);
        var url = $"/api/v1/locations?q=Cache+{tag}";

        // cached empty result for this query ...
        Assert.Empty((await (await GetAsync(url, cache: true)).Content.ReadFromJsonAsync<Sunset.Application.Common.CursorPagedResult<LocationResponse>>())!.Items);
        await RenameBehindTheApiAsync(location.Id, $"Cache {tag}");

        // ... a different query string is a different entry and sees the change ...
        var other = (await (await GetAsync($"/api/v1/locations?q=Cache+{tag}&limit=5", cache: true)).Content.ReadFromJsonAsync<Sunset.Application.Common.CursorPagedResult<LocationResponse>>())!;
        Assert.Single(other.Items);

        // ... and so is a different Origin (the cached response carries that origin's CORS header)
        var fromBrowser = await GetAsync(url, cache: true, configure: r => r.Headers.TryAddWithoutValidation("Origin", "http://localhost:3000"));
        Assert.Single((await fromBrowser.Content.ReadFromJsonAsync<Sunset.Application.Common.CursorPagedResult<LocationResponse>>())!.Items);
    }

    [Fact]
    public async Task APhotoOrACity_ChangesAppearAtOnce_OnTheCityPageToo()
    {
        var tag = Tag();
        var city = await WithDbAsync(async db =>
        {
            var created = new City(CityKind.Municipality, $"Cacheada{tag}", $"Cacheada{tag}", null, $"cacheada{tag}", "XX", "ZC", $"cacheada-{tag}-zc", "test", null, null, null, -20, -40, "America/Sao_Paulo", 0);
            db.Cities.Add(created);
            await db.SaveChangesAsync();
            return created;
        });
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var created = (await (await owner.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest($"Local {tag}", -20, -40, CityId: city.Id))).Content.ReadFromJsonAsync<LocationResponse>())!;

        var before = (await (await GetAsync($"/api/v1/cities/{city.Slug}", cache: true)).Content.ReadFromJsonAsync<CityDetailResponse>())!;
        Assert.Equal(0, before.PhotosCount);

        await CreatePhotoAsync(owner, created.Id);     // goes through the API: evicts the city caches

        var after = (await (await GetAsync($"/api/v1/cities/{city.Slug}", cache: true)).Content.ReadFromJsonAsync<CityDetailResponse>())!;
        Assert.Equal(1, after.PhotosCount);
    }

    // ---------------------------------------------------------------- rate limits

    [Fact]
    public async Task AListingWithoutASearch_IsNotRateLimited()
    {
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/v1/locations?limit=5")).StatusCode);
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/v1/cities?includeEmpty=true&sort=population&limit=5")).StatusCode);
    }

    [Fact]
    public async Task ASearch_IsStillLimitedPerClient()
    {
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 45; i++)
            last = (await GetAsync("/api/v1/locations?q=ab")).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);

        HttpStatusCode lastCity = HttpStatusCode.OK;
        for (var i = 0; i < 45; i++)
            lastCity = (await GetAsync("/api/v1/cities?q=ab&includeEmpty=true&sort=population")).StatusCode;
        // (the cities' search is its own policy, so it has its own bucket for the same address)
        Assert.Equal(HttpStatusCode.TooManyRequests, lastCity);
    }

    [Fact]
    public async Task TheSiteServersKey_GetsABucketOfItsOwn_AndAWrongKeyDoesNot()
    {
        for (var i = 0; i < 80; i++)
        {
            var response = await GetAsync("/api/v1/locations?q=ab", configure: r => r.Headers.Add("X-Service-Key", SunsetApiFactory.ServiceApiKey));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 45; i++)
            last = (await GetAsync("/api/v1/locations?q=ab", configure: r => r.Headers.Add("X-Service-Key", "wrong-key-wrong-key-wrong-key-123456"))).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task TheServiceKey_BuysCapacityOnly_NoAccess()
    {
        var response = await GetAsync("/api/v1/users/me/legal-acceptances", configure: r => r.Headers.Add("X-Service-Key", SunsetApiFactory.ServiceApiKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

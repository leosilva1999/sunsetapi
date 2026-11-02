using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationDuplicateTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static int _slot;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<City> NewMunicipalityAsync() =>
        await WithDbAsync(async db =>
        {
            var tag = Tag();
            var city = new City(CityKind.Municipality, $"Dup{tag}", $"Dup{tag}", null, $"dup{tag}", "XX", "ZD", $"dup{tag}-zd", "test", null, null, null, -20, -40, "UTC", 0);
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            return city;
        });

    // A private spot in the open ocean per test (and per call), so no other test's locations are ever "close".
    private static (double Lat, double Lng) FreshSpot() => (-55.0, -100.0 + 7.0 * (Interlocked.Increment(ref _slot) % 20));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string name, City city, double lat, double lng, bool confirm = false) =>
        client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, lat, lng, CityId: city.Id, ConfirmDistinct: confirm));

    private static async Task<string[]> CandidatesAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errors").GetProperty("candidateLocationIds").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public async Task ALocationRightNextToAnother_Is409WithItsId_UntilTheClientConfirms()
    {
        var city = await NewMunicipalityAsync();
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (lat, lng) = FreshSpot();
        var existing = (await (await PostAsync(client, $"Mirante {Tag()}", city, lat, lng)).Content.ReadFromJsonAsync<LocationResponse>())!;

        var blocked = await PostAsync(client, $"Outro nome {Tag()}", city, lat, lng + 0.001);   // ~60 m away
        var candidates = await CandidatesAsync(blocked);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal([existing.Id.ToString()], candidates);
        Assert.Contains("requestId", await blocked.Content.ReadAsStringAsync());

        var confirmed = await PostAsync(client, $"Outro nome {Tag()}", city, lat, lng + 0.001, confirm: true);
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
    }

    [Fact]
    public async Task ThePlaceFarEnoughAway_IsNotACandidate()
    {
        var city = await NewMunicipalityAsync();
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (lat, lng) = FreshSpot();
        await PostAsync(client, $"Mirante {Tag()}", city, lat, lng);

        var far = await PostAsync(client, $"Vizinho {Tag()}", city, lat, lng + 0.005);   // ~320 m: beyond 150 m

        Assert.Equal(HttpStatusCode.Created, far.StatusCode);
    }

    [Fact]
    public async Task TheSameNameInTheSameMunicipality_Is409EvenFarApart_IgnoringAccentsAndCase()
    {
        var city = await NewMunicipalityAsync();
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (lat, lng) = FreshSpot();
        var (lat2, lng2) = FreshSpot();
        var name = $"Praia do Pôr {Tag()}";
        var existing = (await (await PostAsync(client, name, city, lat, lng)).Content.ReadFromJsonAsync<LocationResponse>())!;

        var blocked = await PostAsync(client, name.ToUpperInvariant().Replace("Ô", "O"), city, lat2, lng2);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal([existing.Id.ToString()], await CandidatesAsync(blocked));
    }

    [Fact]
    public async Task TheSameNameInAnotherMunicipality_IsFine()
    {
        var one = await NewMunicipalityAsync();
        var other = await NewMunicipalityAsync();
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (lat, lng) = FreshSpot();
        var (lat2, lng2) = FreshSpot();
        var name = $"Praia Central {Tag()}";
        await PostAsync(client, name, one, lat, lng);

        var created = await PostAsync(client, name, other, lat2, lng2);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task ARemovedLocation_IsNoLongerACandidate()
    {
        var city = await NewMunicipalityAsync();
        var (_, client) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var (lat, lng) = FreshSpot();
        var name = $"Gruta {Tag()}";
        var existing = (await (await PostAsync(client, name, city, lat, lng)).Content.ReadFromJsonAsync<LocationResponse>())!;
        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{existing.Id}")).EnsureSuccessStatusCode();

        var again = await PostAsync(client, name, city, lat, lng);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }
}

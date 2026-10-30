using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationCityEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    // Names are made unique per test (a tag), so the legacy text can be matched against exactly one city.
    private Task<City> SeedAsync(string name, string state, double lat = -20, double lng = -40, CityKind kind = CityKind.Municipality, Guid? parentId = null, string? alternateNames = null) =>
        WithDbAsync(async db =>
        {
            var searchText = Sunset.Application.Cities.Import.CityImportPlanner.BuildSearchText(name, name, alternateNames);
            var city = new City(kind, name, name, alternateNames, searchText, "XX", state, $"{SlugGenerator.Slugify(name)}-{Tag()}",
                "test", null, null, parentId, lat, lng, "America/Sao_Paulo", 0);
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            return city;
        });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, CreateLocationRequest request) =>
        await client.PostAsJsonAsync("/api/v1/locations", request);

    private static async Task<LocationResponse> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LocationResponse>())!;
    }

    // ---------------------------------------------------------------- registering a location

    [Fact]
    public async Task WithACityId_TheLocationIsTiedToIt_AndTheLegacyTextBecomesCanonical()
    {
        var tag = Tag();
        var city = await SeedAsync($"Cidade{tag}", "ZA");
        var (auth, user) = await RegisterAndAuthenticateAsync();

        var location = await CreatedAsync(await PostAsync(user, new CreateLocationRequest($"Praia {tag}", -20, -40, "texto antigo", city.Id)));

        Assert.Equal($"Cidade{tag} - ZA", location.City);
        Assert.Equal(city.Slug, location.CitySlug);
        Assert.Equal($"Cidade{tag}", location.CityName);
        Assert.Equal("ZA", location.State);
        Assert.Null(location.LocalitySlug);
        Assert.Equal((0, 0, null), (location.RatingsCount, location.PhotosCount, location.CoverImageUrl));

        // who registered it is recorded (moderation only: the response does not carry it)
        var stored = await WithDbAsync(db => db.Locations.AsNoTracking().SingleAsync(l => l.Id == location.Id));
        Assert.Equal(auth.User.Id, stored.CreatedByUserId);
        Assert.Equal(city.Id, stored.CityId);
    }

    [Fact]
    public async Task WithALocalityId_TheMunicipalityIsItsParent_AndBothAreReturned()
    {
        var tag = Tag();
        var city = await SeedAsync($"Cidade{tag}", "ZB");
        var locality = await SeedAsync($"Vila{tag}", "ZB", kind: CityKind.Locality, parentId: city.Id);
        var (_, user) = await RegisterAndAuthenticateAsync();

        var location = await CreatedAsync(await PostAsync(user, new CreateLocationRequest($"Ponto {tag}", -20, -40, CityId: locality.Id)));

        Assert.Equal(city.Slug, location.CitySlug);
        Assert.Equal(locality.Slug, location.LocalitySlug);
        Assert.Equal($"Vila{tag}", location.LocalityName);
        Assert.Equal($"Cidade{tag} - ZB", location.City);
    }

    [Fact]
    public async Task AnUnknownOrInactiveCityId_IsABadRequest()
    {
        var tag = Tag();
        var gone = await SeedAsync($"Gone{tag}", "ZC");
        await WithDbAsync(async db =>
        {
            var city = await db.Cities.SingleAsync(c => c.Id == gone.Id);
            city.Deactivate();
            await db.SaveChangesAsync();
            return 0;
        });
        var (_, user) = await RegisterAndAuthenticateAsync();

        var unknown = await PostAsync(user, new CreateLocationRequest("X", -20, -40, CityId: Guid.NewGuid()));
        var inactive = await PostAsync(user, new CreateLocationRequest("X", -20, -40, CityId: gone.Id));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("CityId", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
    }

    [Fact]
    public async Task WithNeitherCityNorCityId_IsABadRequest()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();

        var response = await PostAsync(user, new CreateLocationRequest("Praia", -20, -40));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LegacyText_ThatMatchesOneMunicipality_IsLinkedOnItsOwn()
    {
        var tag = Tag();
        var city = await SeedAsync($"Unica{tag}", "ZD");
        var (_, user) = await RegisterAndAuthenticateAsync();

        var withState = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("A", -20, -40, $"unica{tag} - zd")));
        var withoutState = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("B", -20, -40, $"Única{tag}")));

        Assert.All(new[] { withState, withoutState }, l =>
        {
            Assert.Equal(city.Slug, l.CitySlug);
            Assert.Equal($"Unica{tag} - ZD", l.City);
        });
    }

    [Fact]
    public async Task LegacyText_OfHomonyms_IsSettledByThePin_OrLeftAsTyped()
    {
        var tag = Tag();
        var north = await SeedAsync($"Homonima{tag}", "ZE", lat: 10, lng: 10);
        await SeedAsync($"Homonima{tag}", "ZF", lat: -40, lng: -70);
        var (_, user) = await RegisterAndAuthenticateAsync();

        var near = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("Perto", 10.1, 10.1, $"Homonima{tag}")));
        var lost = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("Perdido", -20, -20, $"Homonima{tag}")));

        Assert.Equal(north.Slug, near.CitySlug);          // clearly nearer to one of them
        Assert.Null(lost.CitySlug);                        // near neither: a person decides
        Assert.Equal($"Homonima{tag}", lost.City);         // the text stays as typed
    }

    [Fact]
    public async Task LegacyText_ThatOnlyALocalityBears_IsNeverLinkedOnItsOwn()
    {
        var tag = Tag();
        var city = await SeedAsync($"Pai{tag}", "ZG", lat: 5, lng: 5);
        await SeedAsync($"Filha{tag}", "ZG", lat: 5, lng: 5, kind: CityKind.Locality, parentId: city.Id);
        var (_, user) = await RegisterAndAuthenticateAsync();

        var location = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("Ponto", 5, 5, $"Filha{tag} - ZG")));

        Assert.Null(location.CitySlug);
        Assert.Equal($"Filha{tag} - ZG", location.City);
    }

    [Fact]
    public async Task TheTextOnlyRequest_StillWorks_ForClientsThatHaveNotMoved()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();

        var location = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("Praia", -27, -48, "Floripa")));

        Assert.Equal("Floripa", location.City);
        Assert.Null(location.CitySlug);
    }

    // ---------------------------------------------------------------- reading locations

    [Fact]
    public async Task EveryLocationRead_CarriesTheCityAndTheCounts()
    {
        var tag = Tag();
        var city = await SeedAsync($"Leitura{tag}", "ZH");
        var (_, user) = await RegisterAndAuthenticateAsync();
        var created = await CreatedAsync(await PostAsync(user, new CreateLocationRequest($"Praia {tag}", -20, -40, CityId: city.Id)));
        var photo = await CreatePhotoAsync(user, created.Id);
        var (_, rater) = await RegisterAndAuthenticateAsync();
        (await rater.PostAsJsonAsync($"/api/v1/locations/{created.Id}/ratings", new CreateRatingRequest(4))).EnsureSuccessStatusCode();

        var byId = (await CreateClient().GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{created.Id}"))!;
        var search = (await CreateClient().GetFromJsonAsync<CursorPagedResult<LocationResponse>>($"/api/v1/locations?q=Praia+{tag}"))!;

        foreach (var l in new[] { byId, search.Items.Single() })
        {
            Assert.Equal(city.Slug, l.CitySlug);
            Assert.Equal("ZH", l.State);
            Assert.Equal(1, l.RatingsCount);
            Assert.Equal(1, l.PhotosCount);
            Assert.Equal(photo.ImageUrl, l.CoverImageUrl);
        }
    }

    // ---------------------------------------------------------------- the moderation review queue

    [Fact]
    public async Task TheReviewQueue_ExplainsEachUnlinkedLocation()
    {
        var tag = Tag();
        var (author, user) = await RegisterAndAuthenticateAsync();
        var home = await SeedAsync($"Casa{tag}", "ZI", lat: 1, lng: 1);
        await SeedAsync($"Casa{tag}", "ZJ", lat: -50, lng: -70);   // a homonym far away
        var parent = await SeedAsync($"Dona{tag}", "ZK", lat: 3, lng: 3);
        var village = await SeedAsync($"Aldeia{tag}", "ZK", lat: 3, lng: 3, kind: CityKind.Locality, parentId: parent.Id);

        var none = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("N", 0, 0, $"Nada{tag}")));
        var ambiguous = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("A", -20, -20, $"Casa{tag}")));
        var viaLocality = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("L", 3, 3, $"Aldeia{tag} - ZK")));
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var queue = (await moderator.GetFromJsonAsync<List<UnresolvedCityLocationResponse>>("/api/v1/moderation/locations/unresolved-city?limit=50"))!;

        var noneItem = queue.Single(q => q.Location.Id == none.Id);
        Assert.Equal("none", noneItem.Reason);
        Assert.Equal(author.User.Id, noneItem.CreatedByUserId);

        var ambiguousItem = queue.Single(q => q.Location.Id == ambiguous.Id);
        Assert.Equal("ambiguous", ambiguousItem.Reason);
        Assert.Contains(ambiguousItem.Candidates, c => c.Slug == home.Slug);
        Assert.Equal(2, ambiguousItem.Candidates.Count);

        var localityItem = queue.Single(q => q.Location.Id == viaLocality.Id);
        Assert.Equal("locality", localityItem.Reason);
        Assert.Equal(village.Slug, localityItem.SuggestedLocality!.Slug);
        Assert.Equal(parent.Slug, localityItem.SuggestedCity!.Slug);
    }

    [Fact]
    public async Task TheReviewQueue_IsForModeratorsOnly()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().GetAsync("/api/v1/moderation/locations/unresolved-city")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/moderation/locations/unresolved-city")).StatusCode);
    }

    [Fact]
    public async Task AModerator_LinksALocation_AndItLeavesTheQueue_AndIsAudited()
    {
        var tag = Tag();
        var city = await SeedAsync($"Destino{tag}", "ZL");
        var village = await SeedAsync($"Vila{tag}", "ZL", kind: CityKind.Locality, parentId: city.Id);
        var (_, user) = await RegisterAndAuthenticateAsync();
        var location = await CreatedAsync(await PostAsync(user, new CreateLocationRequest($"Ponto {tag}", 0, 0, $"Texto{tag}")));
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var toMunicipality = await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/city", new ChangeLocationCityRequest(city.Id));
        Assert.Equal(HttpStatusCode.OK, toMunicipality.StatusCode);
        var linked = (await toMunicipality.Content.ReadFromJsonAsync<LocationResponse>())!;
        Assert.Equal(city.Slug, linked.CitySlug);
        Assert.Equal($"Destino{tag} - ZL", linked.City);

        var queue = (await moderator.GetFromJsonAsync<List<UnresolvedCityLocationResponse>>("/api/v1/moderation/locations/unresolved-city?limit=50"))!;
        Assert.DoesNotContain(queue, q => q.Location.Id == location.Id);

        // moving it down to a locality keeps the municipality
        var toLocality = (await (await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/city", new ChangeLocationCityRequest(village.Id)))
            .Content.ReadFromJsonAsync<LocationResponse>())!;
        Assert.Equal(city.Slug, toLocality.CitySlug);
        Assert.Equal(village.Slug, toLocality.LocalitySlug);

        var actions = (await moderator.GetFromJsonAsync<CursorPagedResult<ModerationActionResponse>>("/api/v1/moderation/actions?limit=50"))!;
        var audits = actions.Items.Where(a => a.ActionType == ModerationActionType.LocationCityChanged && a.TargetDescription == $"Location:{location.Id}").ToList();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, a => a.Notes!.Contains($"Texto{tag} -> Destino{tag} - ZL"));
        Assert.Contains(audits, a => a.Notes!.Contains($"(locality: Vila{tag})"));
    }

    [Fact]
    public async Task ChangingTheCity_RejectsBadInput_AndRegularUsers()
    {
        var tag = Tag();
        var city = await SeedAsync($"Alvo{tag}", "ZM");
        var (_, user) = await RegisterAndAuthenticateAsync();
        var location = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("P", 0, 0, $"X{tag}")));
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/city", new ChangeLocationCityRequest(city.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/city", new ChangeLocationCityRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}/city", new ChangeLocationCityRequest(Guid.Empty))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{Guid.NewGuid()}/city", new ChangeLocationCityRequest(city.Id))).StatusCode);
    }

    [Fact]
    public async Task MergingTwoLocations_KeepsTheTargetsCity()
    {
        var tag = Tag();
        var first = await SeedAsync($"Primeira{tag}", "ZN");
        var second = await SeedAsync($"Segunda{tag}", "ZN");
        var (_, user) = await RegisterAndAuthenticateAsync();
        var duplicate = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("Dup", -20, -40, CityId: first.Id)));
        var target = await CreatedAsync(await PostAsync(user, new CreateLocationRequest("Alvo", -20, -40, CityId: second.Id)));
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var merged = (await (await moderator.PostAsJsonAsync($"/api/v1/moderation/locations/{duplicate.Id}/merge", new MergeLocationsRequest(target.Id)))
            .Content.ReadFromJsonAsync<LocationResponse>())!;

        Assert.Equal(second.Slug, merged.CitySlug);
    }
}

using System.Net;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationDetailsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ALocation_CanBeRegisteredWithADescriptionAndACategory_AndTheyComeBack()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var created = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest($"Detalhes {Guid.NewGuid():N}"[..20], -20, -40, "Vitoria", Description: "Vista aberta para o mar.", Category: LocationCategory.Viewpoint));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadAsStringAsync();
        Assert.Contains("\"category\":\"Viewpoint\"", body);   // by name, not by number

        var location = (await created.Content.ReadFromJsonAsync<LocationResponse>())!;
        var again = (await CreateClient().GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{location.Id}"))!;
        Assert.Equal("Vista aberta para o mar.", again.Description);
        Assert.Equal(LocationCategory.Viewpoint, again.Category);
    }

    [Fact]
    public async Task ATooLongDescriptionOrAnUnknownCategory_IsA400()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var tooLong = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest("Longa", -20, -40, "Vitoria", Description: new string('a', 1001)));
        var unknown = await client.PostAsync("/api/v1/locations", JsonContent.Create(new { name = "Estranha", latitude = -20, longitude = -40, city = "Vitoria", category = "Spaceport" }));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task AModerator_EditsDescriptionAndCategory_ACommonUserCannot_AndAnEmptyBodyIsA400()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var location = await CreateLocationAsync(owner);

        var denied = await owner.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}", new UpdateLocationDetailsRequest("x"));
        var empty = await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}", new UpdateLocationDetailsRequest());
        var edited = await moderator.PatchAsJsonAsync($"/api/v1/moderation/locations/{location.Id}", new UpdateLocationDetailsRequest("Praia calma.", LocationCategory.Beach));

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        edited.EnsureSuccessStatusCode();
        var fresh = (await CreateClient().GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{location.Id}"))!;
        Assert.Equal("Praia calma.", fresh.Description);
        Assert.Equal(LocationCategory.Beach, fresh.Category);
    }
}

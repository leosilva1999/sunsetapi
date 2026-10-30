using System.Net.Http.Json;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class PhotoDimensionsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ANewPhoto_ComesBackWithItsSize_OnCreateDetailAndFeed()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = (await (await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest($"Tamanho {Guid.NewGuid():N}"[..20], -20, -40, "Vitoria")))
            .Content.ReadFromJsonAsync<LocationResponse>())!;

        var created = await CreatePhotoAsync(client, location.Id);   // the suite's fake storage reports 1200 x 800

        Assert.Equal((1200, 800), (created.Width, created.Height));
        var detail = (await CreateClient().GetFromJsonAsync<PhotoResponse>($"/api/v1/photos/{created.Id}"))!;
        Assert.Equal((1200, 800), (detail.Width, detail.Height));
    }
}

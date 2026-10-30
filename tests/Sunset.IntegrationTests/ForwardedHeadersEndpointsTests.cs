using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class ForwardedHeadersEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<JsonElement> GetClientInfoAsync(HttpClient client, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/client-ip");
        if (forwardedFor is not null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task ClientIp_FollowsTheAddressTheProxyAppended()
    {
        var info = await GetClientInfoAsync(CreateClient(), "203.0.113.7");

        Assert.Equal("203.0.113.7", info.GetProperty("clientIp").GetString());
    }

    [Fact]
    public async Task ClientIp_IgnoresEntriesTheClientForgedOnTheLeft()
    {
        // A visitor can send "X-Forwarded-For: 1.1.1.1"; the proxy then APPENDS the real address.
        var info = await GetClientInfoAsync(CreateClient(), "1.1.1.1, 203.0.113.9");

        Assert.Equal("203.0.113.9", info.GetProperty("clientIp").GetString());
        Assert.Equal(1, info.GetProperty("forwardLimit").GetInt32());
        Assert.Equal("1.1.1.1", info.GetProperty("remainingForwardedFor").GetString());
    }

    [Fact]
    public async Task ClientIp_WithoutTheHeader_IsStillAnswered()
    {
        var info = await GetClientInfoAsync(CreateClient(), null);

        Assert.Equal("http", info.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task SearchRateLimit_IsKeyedPerClientIp_NotPerProxy()
    {
        var client = CreateClient();
        var heavy = "198.51.100.10";
        var other = "198.51.100.20";

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 45; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/locations?q=ab");
            request.Headers.Add("X-Forwarded-For", heavy);
            last = (await client.SendAsync(request)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);

        // Another visitor behind the same proxy is not affected.
        using var fresh = new HttpRequestMessage(HttpMethod.Get, "/api/v1/locations?q=ab");
        fresh.Headers.Add("X-Forwarded-For", other);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(fresh)).StatusCode);
    }
}

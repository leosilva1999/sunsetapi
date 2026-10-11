using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class ErrorResponseTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private const string RequestIdHeader = "X-Request-Id";

    [Fact]
    public async Task NotFound_ReturnsRequestIdInBodyMatchingTheHeader()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/locations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = body.GetProperty("requestId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        Assert.Equal(requestId, Assert.Single(response.Headers.GetValues(RequestIdHeader)));
    }

    [Fact]
    public async Task ValidationFailure_KeepsErrorsAndAddsRequestId()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Validation failed.", body.GetProperty("title").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("Email", out _));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("requestId").GetString()));
    }

    [Fact]
    public async Task EmptyBodyErrors_StillCarryTheRequestIdHeader()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/photos/upload-url", new { contentType = "image/png" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains(RequestIdHeader));
    }

    [Fact]
    public async Task EachRequestGetsItsOwnRequestId()
    {
        var client = CreateClient();

        var first = await client.GetAsync($"/api/v1/locations/{Guid.NewGuid()}");
        var second = await client.GetAsync($"/api/v1/locations/{Guid.NewGuid()}");

        Assert.NotEqual(
            first.Headers.GetValues(RequestIdHeader).Single(),
            second.Headers.GetValues(RequestIdHeader).Single());
    }

    [Fact]
    public async Task ClientSuppliedRequestId_IsIgnored()
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/locations/{Guid.NewGuid()}");
        request.Headers.Add(RequestIdHeader, "forged-id");

        var response = await client.SendAsync(request);

        Assert.NotEqual("forged-id", response.Headers.GetValues(RequestIdHeader).Single());
    }
}

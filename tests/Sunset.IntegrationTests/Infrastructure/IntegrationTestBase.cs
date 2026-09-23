using System.Net.Http.Headers;
using System.Net.Http.Json;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Locations;

namespace Sunset.IntegrationTests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase(SunsetApiFactory factory)
{
    protected HttpClient CreateClient() => factory.CreateClient();

    // Every test gets its own throwaway user/email so tests can run against a database shared
    // with the rest of the suite without colliding (no per-test DB reset).
    protected static string UniqueEmail([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"{caller.ToLowerInvariant()}-{Guid.NewGuid():N}@sunset-tests.dev";

    protected async Task<(AuthResponse Auth, HttpClient Client)> RegisterAndAuthenticateAsync(string? name = null, string? email = null)
    {
        var client = CreateClient();
        var request = new RegisterRequest(name ?? "Test User", email ?? UniqueEmail(), "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (auth, client);
    }

    protected static async Task<LocationResponse> CreateLocationAsync(HttpClient authenticatedClient, string? name = null)
    {
        var request = new CreateLocationRequest(name ?? $"Location {Guid.NewGuid():N}", -27.6, -48.5, "Florianopolis");
        var response = await authenticatedClient.PostAsJsonAsync("/api/v1/locations", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LocationResponse>())!;
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Locations;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence;

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

    // Writes the role directly to the database, bypassing the API - there is no endpoint to
    // create the *first* Moderator/Admin (promotion itself requires an existing Admin), so tests
    // bootstrap the same way a fresh production deploy would have to. Re-logs-in afterwards
    // because the role claim is baked into the JWT at issuance time, not re-read on every request.
    protected async Task<(AuthResponse Auth, HttpClient Client)> RegisterAndAuthenticateWithRoleAsync(UserRole role, string? name = null, string? email = null)
    {
        email ??= UniqueEmail();
        var (auth, _) = await RegisterAndAuthenticateAsync(name, email);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SunsetDbContext>();
            var user = await db.Users.FirstAsync(u => u.Id == auth.User.Id);
            user.ChangeRole(role);
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        loginResponse.EnsureSuccessStatusCode();

        var refreshedAuth = (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAuth.AccessToken);

        return (refreshedAuth, client);
    }
}

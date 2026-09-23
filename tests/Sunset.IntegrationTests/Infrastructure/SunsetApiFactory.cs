using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sunset.Infrastructure.Persistence;
using Testcontainers.MySql;

namespace Sunset.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (Program.cs) against a disposable MySQL container - a real engine is
/// required because location search uses MySQL FULLTEXT (<see cref="Microsoft.EntityFrameworkCore.MySqlDbFunctionsExtensions.Match"/>),
/// which has no InMemory/SQLite equivalent. One container is shared across all tests in the
/// "Integration" collection (see <see cref="IntegrationTestCollection"/>) - spinning up MySQL
/// per test class would make the suite far slower for no isolation benefit, since every test
/// already creates its own uniquely-named data.
/// </summary>
public class SunsetApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mySqlContainer = new MySqlBuilder("mysql:8.0")
        .WithDatabase("sunset_test")
        .Build();

    public async Task InitializeAsync()
    {
        await _mySqlContainer.StartAsync();

        // Program.cs reads ConnectionStrings/Jwt/Storage config synchronously, before
        // WebApplicationBuilder.Build() is called - too early for WebApplicationFactory's
        // usual ConfigureWebHost/ConfigureAppConfiguration hook, which only injects config at
        // Build() time. Environment variables are already loaded into builder.Configuration by
        // WebApplication.CreateBuilder() itself, so they're visible in time; that's why plain
        // config overrides don't work here and these are set as process env vars instead.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _mySqlContainer.GetConnectionString());
        // Base64 of "test-only-secret-key-for-sunset-api-integration-tests" - never used outside this test process.
        Environment.SetEnvironmentVariable("Jwt__Secret", "dGVzdC1vbmx5LXNlY3JldC1rZXktZm9yLXN1bnNldC1hcGktaW50ZWdyYXRpb24tdGVzdHM=");
        Environment.SetEnvironmentVariable("Storage__BucketName", "sunset-photos-test");
        Environment.SetEnvironmentVariable("Storage__Region", "us-east-1");
        Environment.SetEnvironmentVariable("Storage__ServiceUrl", "http://localhost:4566");
        Environment.SetEnvironmentVariable("Storage__ForcePathStyle", "true");
        Environment.SetEnvironmentVariable("Storage__PublicBaseUrl", "http://localhost:4566/sunset-photos-test");
        Environment.SetEnvironmentVariable("Storage__AccessKey", "test");
        Environment.SetEnvironmentVariable("Storage__SecretKey", "test");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:3000");

        // Triggers host build (reads the env vars set above), then applies migrations so the
        // schema - FULLTEXT index included - actually exists.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SunsetDbContext>();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _mySqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sunset.Application.Interfaces;
using Sunset.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Sunset.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (Program.cs) against a disposable PostgreSQL container - a real engine is
/// required because location search uses a tsvector/GIN full-text index and the "near me" filter
/// computes Haversine in SQL, neither of which has an InMemory/SQLite equivalent. One container
/// is shared across all tests in the "Integration" collection (see <see cref="IntegrationTestCollection"/>) - spinning up Postgres
/// per test class would make the suite far slower for no isolation benefit, since every test
/// already creates its own uniquely-named data.
/// </summary>
public class SunsetApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("sunset_test")
        .Build();

    public FakeEmailSender EmailSender { get; } = new();

    /// <summary>The secret the suite's server accepts as X-Service-Key.</summary>
    public const string ServiceApiKey = "test-only-service-key-0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The suite shouldn't depend on Mailpit running or on any network - swap the real SMTP
        // sender for an in-memory one tests can inspect.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // Test photo/avatar URLs point at storage that doesn't exist in this suite (no
            // LocalStack): the real sanitizer would fail to download them.
            services.RemoveAll<IUploadedImageSanitizer>();
            services.AddSingleton<IUploadedImageSanitizer>(new AlwaysCleanImageSanitizer());
        });
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        // Program.cs reads ConnectionStrings/Jwt/Storage config synchronously, before
        // WebApplicationBuilder.Build() is called - too early for WebApplicationFactory's
        // usual ConfigureWebHost/ConfigureAppConfiguration hook, which only injects config at
        // Build() time. Environment variables are already loaded into builder.Configuration by
        // WebApplication.CreateBuilder() itself, so they're visible in time; that's why plain
        // config overrides don't work here and these are set as process env vars instead.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _postgresContainer.GetConnectionString());
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
        Environment.SetEnvironmentVariable("Frontend__BaseUrl", "https://sunset-tests.dev");
        Environment.SetEnvironmentVariable("Service__ApiKey", ServiceApiKey);

        // Triggers host build (reads the env vars set above), then applies migrations so the
        // schema - full-text index included - actually exists.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SunsetDbContext>();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}

internal sealed class AlwaysCleanImageSanitizer : IUploadedImageSanitizer
{
    public Task<bool> SanitizeAsync(string imageUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<Sunset.Application.Common.ImageDimensions?> SanitizeAndMeasureAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<Sunset.Application.Common.ImageDimensions?>(new Sunset.Application.Common.ImageDimensions(1200, 800));
}

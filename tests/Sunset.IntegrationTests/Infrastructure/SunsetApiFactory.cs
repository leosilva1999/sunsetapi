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

    /// <summary>Stands in for Expo: records every push the API sends and never touches the network.</summary>
    public FakePushSender PushSender { get; } = new();

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
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(PushSender);

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
        Environment.SetEnvironmentVariable("Push__Enabled", "true");
        Environment.SetEnvironmentVariable("Push__GroupWindowSeconds", "3");   // production uses 60; the suite cannot wait that long

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

/// <summary>
/// A push provider for tests: keeps every message it was given. A token containing "dead" is reported as
/// DeviceNotRegistered (the app was uninstalled); any other is accepted with a ticket. Receipts can be scripted.
/// </summary>
public sealed class FakePushSender : IPushSender
{
    private int _tickets;

    public System.Collections.Concurrent.ConcurrentQueue<Sunset.Application.Push.PushMessage> Sent { get; } = new();

    /// <summary>Receipts handed back to the API, by ticket id (empty until a test fills it).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Sunset.Application.Push.PushReceipt> Receipts { get; } = new();

    public Task<IReadOnlyList<Sunset.Application.Push.PushSendResult>> SendAsync(IReadOnlyList<Sunset.Application.Push.PushMessage> messages, CancellationToken cancellationToken = default)
    {
        var results = new List<Sunset.Application.Push.PushSendResult>();
        foreach (var message in messages)
        {
            Sent.Enqueue(message);
            results.Add(message.To.Contains("dead", StringComparison.Ordinal)
                ? Sunset.Application.Push.PushSendResult.Failed(Sunset.Application.Push.PushFailure.DeviceNotRegistered)
                : Sunset.Application.Push.PushSendResult.Accepted($"fake-ticket-{Interlocked.Increment(ref _tickets)}"));
        }

        return Task.FromResult<IReadOnlyList<Sunset.Application.Push.PushSendResult>>(results);
    }

    public Task<IReadOnlyDictionary<string, Sunset.Application.Push.PushReceipt>> GetReceiptsAsync(IReadOnlyCollection<string> ticketIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, Sunset.Application.Push.PushReceipt>>(
            ticketIds.Where(Receipts.ContainsKey).ToDictionary(id => id, id => Receipts[id]));

    /// <summary>The pushes sent to one token so far.</summary>
    public List<Sunset.Application.Push.PushMessage> To(string token) => Sent.Where(m => m.To == token).ToList();
}

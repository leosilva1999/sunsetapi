using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Sunset.API;
using Sunset.API.Health;
using Sunset.API.Middlewares;
using Sunset.API.OpenApi;
using Sunset.Application;
using Sunset.Application.Interfaces;
using Sunset.Infrastructure;
using Sunset.Infrastructure.Persistence;
using Sunset.Infrastructure.Security;
using Sunset.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default logger but keeps the ILogger<T> abstraction, so existing code is
// untouched. Sinks and per-namespace levels live in appsettings ("Serilog" section).
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    // Placeholders for lines written outside a request / before authentication; the real values
    // (pushed via LogContext or the logging scope) are added first and win, since WithProperty
    // only fills in a property that is still absent.
    .Enrich.WithProperty("RequestId", "-")
    .Enrich.WithProperty("UserId", "-"));

// Optional, gitignored override for machine-specific dev values (e.g. Storage:ServiceUrl /
// Storage:PublicBaseUrl pointing at the current LAN IP instead of localhost, so a phone on
// Expo Go can reach LocalStack) - mirrors the mobile app's .env / .env.example pattern.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

// Add services to the container.

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

// Fails fast with every misconfiguration listed at once (JWT secret size, storage keys, email
// provider...) - see StartupConfigurationValidator. In Development the JWT secret comes from
// appsettings.Development.json; elsewhere via user-secrets or the Jwt__Secret environment variable.
StartupConfigurationValidator.ValidateOrThrow(builder.Configuration, builder.Environment.IsProduction());

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // MapInboundClaims = false above means claim types stay exactly as issued - without
            // this, [Authorize(Roles = ...)] would look for the default long-form role claim URI
            // instead of the short "role" claim TokenService actually puts in the JWT.
            RoleClaimType = "role",
        };
    });

builder.Services.AddAuthorization();

const string FrontendCorsPolicy = "Frontend";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Custom response headers are invisible to browser JS unless exposed explicitly.
        .WithExposedHeaders(RequestIdMiddleware.HeaderName));
});

// "Search" policy: protects instant-search-style endpoints (typeahead on every keystroke) from
// being hammered, whether by a runaway client-side loop or a debounce bug - independent of
// whatever debouncing the frontend does, since that's not something the API can rely on.
const string SearchRateLimitPolicy = "Search";
// "Reports" policy: much lower ceiling than Search - filing a report is a deliberate, infrequent
// action, so this exists purely to stop report-flooding abuse, not accommodate legitimate bursts.
const string ReportsRateLimitPolicy = "Reports";
// "PasswordReset" policy: forgot-password sends an email to a third party's inbox on every call
// (even for unknown addresses, since the endpoint always returns 204) - without this, the
// endpoint itself would be a free tool for spamming/harassing an arbitrary email address.
const string PasswordResetRateLimitPolicy = "PasswordReset";
// "ChangePassword" policy: the endpoint verifies the current password, so without a cap a stolen
// access token could be used to brute-force it.
const string ChangePasswordRateLimitPolicy = "ChangePassword";
var rateLimitTitlesByPolicy = new Dictionary<string, string>
{
    [SearchRateLimitPolicy] = "Too many search requests. Please slow down.",
    [ReportsRateLimitPolicy] = "Too many reports. Please slow down.",
    [PasswordResetRateLimitPolicy] = "Too many password reset requests. Please slow down.",
    [ChangePasswordRateLimitPolicy] = "Too many password change attempts. Please try again later.",
};
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(SearchRateLimitPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 40,
            Window = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 5,
            QueueLimit = 0,
        }));

    options.AddPolicy(ReportsRateLimitPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        }));

    // The 15-minute window is deliberately much longer than the whole integration test run, so
    // (unlike Search/Reports, whose shorter windows can cycle mid-suite) it would otherwise trip
    // deterministically partway through Sunset.IntegrationTests instead of just under real abuse.
    var passwordResetPermitLimit = builder.Environment.IsEnvironment("Testing") ? int.MaxValue : 5;
    options.AddPolicy(PasswordResetRateLimitPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = passwordResetPermitLimit,
            Window = TimeSpan.FromMinutes(15),
            SegmentsPerWindow = 3,
            QueueLimit = 0,
        }));

    var changePasswordPermitLimit = builder.Environment.IsEnvironment("Testing") ? int.MaxValue : 5;
    options.AddPolicy(ChangePasswordRateLimitPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = changePasswordPermitLimit,
            Window = TimeSpan.FromMinutes(15),
            SegmentsPerWindow = 3,
            QueueLimit = 0,
        }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers.RetryAfter = "10";

        var policyName = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var title = (policyName is not null && rateLimitTitlesByPolicy.TryGetValue(policyName, out var policyTitle))
            ? policyTitle
            : rateLimitTitlesByPolicy[SearchRateLimitPolicy];

        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiter")
            .LogWarning("Rate limit hit on policy {RateLimitPolicy} from {ClientIp}", policyName, context.HttpContext.Connection.RemoteIpAddress);

        var payload = JsonSerializer.Serialize(new { title, errors = (object?)null, requestId = context.HttpContext.TraceIdentifier });
        await context.HttpContext.Response.WriteAsync(payload, cancellationToken);
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Sunsetsss API v1");
        options.RoutePrefix = "swagger";
    });

    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        using var seedScope = app.Services.CreateScope();
        var seedProvider = seedScope.ServiceProvider;
        await DbSeeder.SeedAsync(
            seedProvider.GetRequiredService<SunsetDbContext>(),
            seedProvider.GetRequiredService<IPasswordHasher>(),
            seedProvider.GetRequiredService<ILogger<Program>>());

        await LocalStackBucketInitializer.EnsureBucketReadyAsync(
            seedProvider.GetRequiredService<IAmazonS3>(),
            seedProvider.GetRequiredService<IOptions<StorageOptions>>().Value,
            seedProvider.GetRequiredService<ILogger<Program>>());
    }
    catch (Exception exception)
    {
        // Without this the failure only reaches stderr as a bare unhandled exception (e.g. when
        // LocalStack is unreachable because the machine's LAN IP changed) and never the log file.
        startupLogger.LogCritical(exception, "Development startup initialization failed (database seed / LocalStack bucket)");
        throw;
    }
}

app.UseMiddleware<RequestIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    // One summary line per request (method, route, status, duration). UserId is read when the
    // request completes, i.e. after authentication has populated the principal.
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        if (httpContext.User.FindFirst("sub")?.Value is { } userId)
            diagnosticContext.Set("UserId", userId);
    };

    // Uptime/orchestrator probes hit /health every few seconds; logging each one would bury the
    // real traffic and grow the log for nothing. They drop to Verbose (below the Information
    // minimum) unless they actually fail.
    options.GetLevel = (httpContext, _, exception) =>
        exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
        : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness: process is up, no dependencies touched. Readiness: also checks the database.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

// Top-level statements generate an internal Program class; WebApplicationFactory<Program>
// in Sunset.IntegrationTests needs it public to bootstrap the test host.
public partial class Program;

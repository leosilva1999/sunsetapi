using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Sunset.API;
using Sunset.API.Caching;
using Sunset.API.Health;
using Sunset.API.Middlewares;
using Sunset.API.OpenApi;
using Sunset.API.Security;
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

        // The JWT is stateless, so a ban would otherwise only bite when the access token
        // expires (<= 15 min). Failing validation here makes it immediate (401); the refresh tokens were
        // revoked by the ban, so the client ends up at the login screen, where login answers 403
        // with the ban reason/end.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId))
                    return;

                var banStatus = context.HttpContext.RequestServices.GetRequiredService<IBanStatusService>();
                if (await banStatus.GetActiveBanAsync(userId, context.HttpContext.RequestAborted) is not null)
                    context.Fail("This account is banned.");
            },
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
// "CitySearch": the same ceiling for the cities' text search (the registration autocomplete).
const string CitySearchRateLimitPolicy = "CitySearch";
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
    [CitySearchRateLimitPolicy] = "Too many search requests. Please slow down.",
    [ReportsRateLimitPolicy] = "Too many reports. Please slow down.",
    [PasswordResetRateLimitPolicy] = "Too many password reset requests. Please slow down.",
    [ChangePasswordRateLimitPolicy] = "Too many password change attempts. Please try again later.",
};
// Trust X-Forwarded-For / -Proto from the platform proxy. The app is only reachable through it, so
// the default "loopback only" trust list (which would ignore the headers entirely) is cleared.
// ForwardLimit decides how many entries are consumed from the RIGHT of X-Forwarded-For: the proxy
// APPENDS the address it saw, so the rightmost entry can't be forged by the client, while anything
// to its left can. 1 = only the entry added by the proxy directly in front of us. If more than one
// trusted proxy sits in front (e.g. a CDN + the platform load balancer), raise it with
// ForwardedHeaders:ForwardLimit - measured with GET /health/client-ip (see docs/API.md).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = Math.Max(1, builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1));
});

// The site's own server presents a secret (X-Service-Key) and gets a bucket of its own - otherwise every visitor
// it renders pages for would share ONE address and one bucket. See ServiceKeyValidator.
builder.Services.AddSingleton<ServiceKeyValidator>();

// Response cache for the public reads. In the Testing environment it is opt-in per request, so tests sharing
// one server don't read each other's cached responses.
builder.Services.AddPublicOutputCache(optInOnly: builder.Environment.IsEnvironment("Testing"));

// Only a real SEARCH is rate limited: a plain listing/paging (what the site's server and the sitemap do) is cheap
// and cacheable, and used to burn the same 40-per-10s budget as a keystroke-by-keystroke autocomplete.
static bool IsLocationSearch(HttpRequest request) =>
    !string.IsNullOrWhiteSpace(request.Query["q"]) || request.Query.ContainsKey("lat") || request.Query.ContainsKey("lng");

static bool IsCitySearch(HttpRequest request) => !string.IsNullOrWhiteSpace(request.Query["q"]);

static RateLimitPartition<string> SearchPartition(HttpContext httpContext, Func<HttpRequest, bool> isSearch)
{
    if (!isSearch(httpContext.Request))
        return RateLimitPartition.GetNoLimiter("not-a-search");

    var fromSiteServer = httpContext.RequestServices.GetRequiredService<ServiceKeyValidator>().IsValid(httpContext.Request);
    return RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: fromSiteServer ? "service" : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = fromSiteServer ? 600 : 40,
            Window = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 5,
            QueueLimit = 0,
        });
}

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(SearchRateLimitPolicy, httpContext => SearchPartition(httpContext, IsLocationSearch));
    options.AddPolicy(CitySearchRateLimitPolicy, httpContext => SearchPartition(httpContext, IsCitySearch));

    // In the integration suite every request comes from the same address, so the whole suite shares one
    // bucket - lift the cap there (same as the password policies below).
    var reportsPermitLimit = builder.Environment.IsEnvironment("Testing") ? int.MaxValue : 10;
    options.AddPolicy(ReportsRateLimitPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = reportsPermitLimit,
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

// Behind Render's proxy the TCP peer is the proxy, not the visitor. Without this, every
// IP-keyed rate limiter (Search, Reports, PasswordReset, ChangePassword) put all users in ONE
// bucket, and the log line "from {ClientIp}" showed the proxy. Must run before anything that reads
// the client address or scheme.
app.UseForwardedHeaders();

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

// The cache goes BEFORE the rate limiter: a response served from memory costs nothing, so it must not spend the
// visitor's quota. (And before authentication on purpose - a request that carries a token is never served from or
// stored in the cache, so nothing user-specific is shared.)
app.UseOutputCache();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Inside the cache: stamps Cache-Control + ETag on the cacheable public GETs (only runs on a cache miss).
app.UseMiddleware<PublicCacheHeadersMiddleware>();

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

// Diagnostic for the forwarded-headers setup: shows the caller how the API sees them (nothing but
// their own request). `clientIp` is what the rate limiters key on. The full X-Forwarded-For chain the
// proxy sent is `remainingForwardedFor` (entries left of the consumed ones) followed by `clientIp`, and
// `proxyPeer` is the TCP peer (the platform proxy) - enough to verify the right ForwardedHeaders:ForwardLimit.
app.MapGet("/health/client-ip", (HttpContext context, IOptions<ForwardedHeadersOptions> forwarded) => Results.Ok(new
{
    clientIp = context.Connection.RemoteIpAddress?.ToString(),
    scheme = context.Request.Scheme,
    proxyPeer = context.Request.Headers["X-Original-For"].ToString(),
    remainingForwardedFor = context.Request.Headers["X-Forwarded-For"].ToString(),
    forwardLimit = forwarded.Value.ForwardLimit,
}));

app.Run();

// Top-level statements generate an internal Program class; WebApplicationFactory<Program>
// in Sunset.IntegrationTests needs it public to bootstrap the test host.
public partial class Program;

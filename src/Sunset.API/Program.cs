using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sunset.API.Middlewares;
using Sunset.API.OpenApi;
using Sunset.Application;
using Sunset.Application.Interfaces;
using Sunset.Infrastructure;
using Sunset.Infrastructure.Persistence;
using Sunset.Infrastructure.Security;
using Sunset.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section not found.");

if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
    throw new InvalidOperationException(
        "Jwt:Secret is not configured. In Development it comes from appsettings.Development.json; " +
        "in other environments, set it via 'dotnet user-secrets set \"Jwt:Secret\" \"<value>\"' or the Jwt__Secret environment variable.");

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
        .AllowAnyMethod());
});

// "Search" policy: protects instant-search-style endpoints (typeahead on every keystroke) from
// being hammered, whether by a runaway client-side loop or a debounce bug - independent of
// whatever debouncing the frontend does, since that's not something the API can rely on.
const string SearchRateLimitPolicy = "Search";
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

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers.RetryAfter = "10";

        var payload = JsonSerializer.Serialize(new { title = "Too many search requests. Please slow down.", errors = (object?)null });
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
        options.SwaggerEndpoint("/openapi/v1.json", "Sunset API v1");
        options.RoutePrefix = "swagger";
    });

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

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Infrastructure.ExternalServices;
using Sunset.Infrastructure.Persistence;
using Sunset.Infrastructure.Persistence.Repositories;
using Sunset.Infrastructure.Security;
using Sunset.Infrastructure.Storage;

namespace Sunset.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<SunsetDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddHttpContextAccessor();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IPhotoRepository, PhotoRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IModerationActionRepository, ModerationActionRepository>();
        services.AddScoped<ITermsOfServiceRepository, TermsOfServiceRepository>();

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddHttpClient<ISunsetTimeService, SunsetTimeService>(client =>
        {
            client.BaseAddress = new Uri("https://api.sunrise-sunset.org/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IAmazonS3>(_ => CreateS3Client(configuration.GetSection(StorageOptions.SectionName)));
        services.AddScoped<IPhotoStorageService, S3PhotoStorageService>();
        services.AddScoped<IAvatarStorageService, S3AvatarStorageService>();

        return services;
    }

    private static IAmazonS3 CreateS3Client(IConfigurationSection storageSection)
    {
        var config = new AmazonS3Config { ForcePathStyle = storageSection.GetValue<bool>(nameof(StorageOptions.ForcePathStyle)) };

        var serviceUrl = storageSection[nameof(StorageOptions.ServiceUrl)];
        if (!string.IsNullOrWhiteSpace(serviceUrl))
            config.ServiceURL = serviceUrl;
        else
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(storageSection[nameof(StorageOptions.Region)]);

        var accessKey = storageSection[nameof(StorageOptions.AccessKey)];
        var secretKey = storageSection[nameof(StorageOptions.SecretKey)];

        // Real AWS credentials never live in config - only LocalStack's dummy key/secret do
        // (see StorageOptions). Without them, fall back to the SDK's default credential chain
        // (environment/IAM role), same as AmazonS3Client() with no explicit credentials.
        return !string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonS3Client(accessKey, secretKey, config)
            : new AmazonS3Client(config);
    }
}

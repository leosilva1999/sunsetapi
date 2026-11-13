using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Sunset.Application.Interfaces;
using Sunset.Application.Services;
using Sunset.Application.Validators.Auth;

namespace Sunset.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IPhotoService, PhotoService>();
        services.AddScoped<IModerationService, ModerationService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ILegalAcceptanceService, LegalAcceptanceService>();
        services.AddScoped<ICityService, CityService>();
        services.AddScoped<ISitemapService, SitemapService>();
        services.AddScoped<IStatsService, StatsService>();
        services.AddScoped<IPushDeviceService, PushDeviceService>();
        services.AddScoped<IPushDispatchService, PushDispatchService>();
        services.AddSingleton<Sunset.Application.Push.PushThrottle>();
        services.AddSingleton<Sunset.Application.Push.PushGrouper>();
        services.AddScoped<INotificationSettingsService, NotificationSettingsService>();
        services.AddScoped<Sunset.Application.Cities.CityResolver>();

        return services;
    }
}

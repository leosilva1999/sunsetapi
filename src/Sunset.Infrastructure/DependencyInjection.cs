using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Infrastructure.Persistence;
using Sunset.Infrastructure.Persistence.Repositories;

namespace Sunset.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<SunsetDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IPhotoRepository, PhotoRepository>();

        return services;
    }
}

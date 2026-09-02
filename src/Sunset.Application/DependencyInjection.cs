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

        return services;
    }
}

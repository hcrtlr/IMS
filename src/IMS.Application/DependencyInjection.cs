using IMS.Application.Common.Services;
using IMS.Application.Features.Auth;
using IMS.Application.Features.MasterData;
using Microsoft.Extensions.DependencyInjection;

namespace IMS.Application;

/// <summary>Registers the application-layer use-case services.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ScopeGuard>();

        // Security
        services.AddScoped<AuthService>();

        // Faz 1 - master data
        services.AddScoped<WarehouseService>();
        services.AddScoped<ZoneService>();
        services.AddScoped<LocationService>();
        services.AddScoped<LocationProfileService>();
        services.AddScoped<ItemService>();
        services.AddScoped<ReferenceDataService>();

        return services;
    }
}

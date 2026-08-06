using IMS.Application.Common.Interfaces;
using IMS.Infrastructure.Identity;
using IMS.Infrastructure.Persistence;
using IMS.Infrastructure.Persistence.Repositories;
using IMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IMS.Infrastructure;

/// <summary>Registers every Infrastructure implementation behind its Application contract.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Set ConnectionStrings__Default " +
                "as an environment variable or via dotnet user-secrets.");

        services.AddDbContext<ImsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ImsDbContext).Assembly.FullName);

                // Transient PostgreSQL faults are retried; the execution strategy is what
                // ExecuteInTransactionAsync wraps its transactions in.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            });

            // Every query is read-only unless a service explicitly tracks, which keeps
            // accidental writes out of reporting paths.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ImsDbContext>());
        services.AddScoped<IInventoryRepository, InventoryRepository>();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}

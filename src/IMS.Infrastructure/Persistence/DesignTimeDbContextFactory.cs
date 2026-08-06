using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IMS.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add` build the model without starting the API.
/// Reads the connection string from IMS_MIGRATIONS_CONNECTION when present so a
/// developer can point migrations at a scratch database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ImsDbContext>
{
    public ImsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("IMS_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ims;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ImsDbContext>()
            .UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(ImsDbContext).Assembly.FullName))
            .Options;

        return new ImsDbContext(options);
    }
}

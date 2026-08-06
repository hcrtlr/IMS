using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using IMS.Api.Middleware;
using IMS.Api.Security;
using IMS.Application;
using IMS.Application.Common.Interfaces;
using IMS.Infrastructure;
using IMS.Infrastructure.Identity;
using IMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// Bootstrap logger captures failures that happen before the host is built.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "IMS.Api")
        .WriteTo.Console()
        .WriteTo.File("logs/ims-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

    // ---------------------------------------------------------------------
    // Layers
    // ---------------------------------------------------------------------
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    // ---------------------------------------------------------------------
    // Authentication / authorization
    // ---------------------------------------------------------------------
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? new JwtSettings();

    if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Secret must be configured with at least 32 characters. Set it via " +
            "`dotnet user-secrets set \"Jwt:Secret\" \"<value>\"` or the Jwt__Secret environment variable.");
    }

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                // No leeway: an expired token is expired.
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization(options => options.AddImsPolicies());

    // ---------------------------------------------------------------------
    // MVC + validation
    // ---------------------------------------------------------------------
    builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            // Enums travel as their names, so clients never depend on numeric ordering.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddValidatorsFromAssembly(typeof(IApplicationDbContext).Assembly);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Inventory Management System API",
            Version = "v1",
            Description =
                "Warehouse inventory, inbound and outbound processes. " +
                "Implements the Stajyer Projesi: Inventory Management System specification."
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the token returned by POST /api/auth/login (no 'Bearer ' prefix)."
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        var xmlPath = Path.Combine(AppContext.BaseDirectory,
            $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

        if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);
    });

    // The demo frontend is served from the same origin, so CORS is only needed if a
    // client is hosted elsewhere.
    builder.Services.AddCors(options => options.AddPolicy("DemoClient", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<ImsDbContext>("database");

    var app = builder.Build();

    // ---------------------------------------------------------------------
    // Pipeline
    // ---------------------------------------------------------------------
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "IMS API v1");
            options.DocumentTitle = "IMS API";
        });
    }
    else
    {
        app.UseHsts();
    }

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseCors("DemoClient");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();

    // ---------------------------------------------------------------------
    // Migrate + seed
    // ---------------------------------------------------------------------
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ImsDbContext>();

        if (app.Configuration.GetValue("Database:AutoMigrate", true))
        {
            Log.Information("Applying database migrations...");
            await db.Database.MigrateAsync();
        }

        if (app.Configuration.GetValue("Database:AutoSeed", true))
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync();
        }
    }

    Log.Information("IMS API started");
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "IMS API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so the integration test project can build a test host.</summary>
public partial class Program;

using System.Security.Cryptography;
using IMS.Application.Common.Interfaces;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Entities.Security;
using IMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IMS.Infrastructure.Persistence;

/// <summary>
/// Seeds the reference data the system cannot function without: the seven documented
/// inventory statuses (§5.2) and the four documented units of measure (§4.3), plus a
/// bootstrap account and administrator so the API is reachable on a fresh database.
///
/// Every step is idempotent - running it repeatedly changes nothing.
/// </summary>
public class DatabaseSeeder
{
    private readonly ImsDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ImsDbContext db,
        IPasswordHasher hasher,
        IConfiguration config,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
        _logger = logger;
    }

    // Fixed ids keep seeded reference data stable across environments and migrations.
    private static Guid Id(string s) => new(MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s)));

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedInventoryStatusesAsync(ct);
        await SeedUnitsOfMeasureAsync(ct);
        await SeedBootstrapAccountAsync(ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Doc §5.2. IsAllocatable is what enforces rule §11.4 - only Available stock may
    /// back a normal order; hold, damaged, expired, quarantined and blocked stock cannot.
    /// </summary>
    private async Task SeedInventoryStatusesAsync(CancellationToken ct)
    {
        var seed = new (string Code, string Name, bool Allocatable, int Order)[]
        {
            (InventoryStatus.Available,   "Available",     true,  1),
            (InventoryStatus.QualityHold, "Quality Hold",  false, 2),
            (InventoryStatus.Damaged,     "Damaged",       false, 3),
            (InventoryStatus.Expired,     "Expired",       false, 4),
            (InventoryStatus.Quarantine,  "Quarantine",    false, 5),
            (InventoryStatus.Returned,    "Returned",      false, 6),
            (InventoryStatus.Blocked,     "Blocked",       false, 7)
        };

        var existing = await _db.InventoryStatuses
            .Select(s => s.Code)
            .ToListAsync(ct);

        foreach (var (code, name, allocatable, order) in seed)
        {
            if (existing.Contains(code)) continue;

            _db.InventoryStatuses.Add(new InventoryStatus
            {
                Id = Id($"status:{code}"),
                Code = code,
                Name = name,
                IsAllocatable = allocatable,
                IsPhysicalStock = true,
                DisplayOrder = order,
                IsActive = true
            });
        }
    }

    /// <summary>Doc §4.3 packaging hierarchy: Each / Pack / Case / Pallet.</summary>
    private async Task SeedUnitsOfMeasureAsync(CancellationToken ct)
    {
        var seed = new (string Code, string Name)[]
        {
            ("EA", "Each"),
            ("PK", "Pack"),
            ("CS", "Case"),
            ("PL", "Pallet")
        };

        var existing = await _db.UnitsOfMeasure.Select(u => u.Code).ToListAsync(ct);

        foreach (var (code, name) in seed)
        {
            if (existing.Contains(code)) continue;

            _db.UnitsOfMeasure.Add(new UnitOfMeasure
            {
                Id = Id($"uom:{code}"),
                Code = code,
                Name = name,
                IsActive = true
            });
        }
    }

    /// <summary>
    /// Creates a first Account and an Admin user, otherwise nobody could authenticate to
    /// create the first anything. The password comes from configuration; when it is not
    /// set a random one is generated and logged once, so no fixed default credential
    /// ever ships.
    /// </summary>
    private async Task SeedBootstrapAccountAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct)) return;

        var accountCode = _config["Seed:AccountCode"] ?? "DEFAULT";
        var accountName = _config["Seed:AccountName"] ?? "Default Account";
        var username = _config["Seed:AdminUsername"] ?? "admin";
        var email = _config["Seed:AdminEmail"] ?? "admin@ims.local";

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == accountCode, ct);
        if (account is null)
        {
            account = new Account
            {
                Id = Id($"account:{accountCode}"),
                Code = accountCode,
                Name = accountName,
                IsActive = true
            };
            _db.Accounts.Add(account);
        }

        var configured = _config["Seed:AdminPassword"];
        var password = string.IsNullOrWhiteSpace(configured) ? GeneratePassword() : configured;

        _db.Users.Add(new ApplicationUser
        {
            Id = Id($"user:{username}"),
            AccountId = account.Id,
            Username = username,
            Email = email,
            FullName = "System Administrator",
            PasswordHash = _hasher.Hash(password),
            Role = UserRole.Admin,
            IsActive = true
        });

        if (string.IsNullOrWhiteSpace(configured))
        {
            _logger.LogWarning(
                "Seeded administrator '{Username}' with a GENERATED password: {Password} — " +
                "record it now and change it. Set Seed:AdminPassword (or " +
                "Seed__AdminPassword as an environment variable) to control this value.",
                username, password);
        }
        else
        {
            _logger.LogInformation("Seeded administrator '{Username}' using the configured password.", username);
        }
    }

    /// <summary>Cryptographically random, URL-safe bootstrap password.</summary>
    private static string GeneratePassword()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', 'A').Replace('/', 'z').TrimEnd('=');
}

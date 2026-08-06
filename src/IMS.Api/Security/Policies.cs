using IMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace IMS.Api.Security;

/// <summary>
/// Named authorization policies, mapping the four roles onto warehouse duties.
/// Not part of the source document - see docs/ASSUMPTIONS.md.
/// </summary>
public static class Policies
{
    /// <summary>Account and user administration.</summary>
    public const string Administration = "Administration";

    /// <summary>Creating and changing master data (items, warehouses, zones, locations).</summary>
    public const string ManageMasterData = "ManageMasterData";

    /// <summary>Day-to-day floor work: receive, putaway, pick, move, count.</summary>
    public const string WarehouseOperations = "WarehouseOperations";

    /// <summary>Approving inventory adjustments and other supervisory sign-off.</summary>
    public const string ApproveAdjustments = "ApproveAdjustments";

    /// <summary>Any authenticated user; read-only endpoints.</summary>
    public const string ReadOnly = "ReadOnly";

    public static AuthorizationOptions AddImsPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Administration, p =>
            p.RequireRole(nameof(UserRole.Admin)));

        options.AddPolicy(ManageMasterData, p =>
            p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.WarehouseManager)));

        // Adjustments must be approved by someone other than a floor operator.
        options.AddPolicy(ApproveAdjustments, p =>
            p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.WarehouseManager)));

        options.AddPolicy(WarehouseOperations, p =>
            p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.WarehouseManager), nameof(UserRole.Operator)));

        options.AddPolicy(ReadOnly, p => p.RequireAuthenticatedUser());

        return options;
    }
}

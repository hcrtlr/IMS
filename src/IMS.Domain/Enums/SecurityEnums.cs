namespace IMS.Domain.Enums;

/// <summary>
/// Roles are NOT in the source document - authentication and authorization were
/// requested separately. Kept deliberately small and mapped to warehouse duties.
/// See docs/ASSUMPTIONS.md.
/// </summary>
public enum UserRole
{
    /// <summary>Full access, including account and user administration.</summary>
    Admin = 1,
    /// <summary>Master data maintenance plus every operational action and approvals.</summary>
    WarehouseManager = 2,
    /// <summary>Day-to-day floor work: receiving, putaway, picking, counting, movements.</summary>
    Operator = 3,
    /// <summary>Read-only access to all queries and reports.</summary>
    Viewer = 4
}

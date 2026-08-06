namespace IMS.Domain.Enums;

// -----------------------------------------------------------------------------
// Section 9 is MISSING from the source document (it jumps from §8 to §10), yet
// cycle counting and stock adjustment are required by §1, the §12 API list and
// Faz 5. Everything in this file is designed by analogy with the documented
// inbound/outbound task patterns. See docs/ASSUMPTIONS.md.
// -----------------------------------------------------------------------------

/// <summary>Scope of a count plan - which slice of the warehouse gets counted.</summary>
public enum CountType
{
    /// <summary>Recurring count of a subset of locations while the warehouse operates.</summary>
    Cycle = 1,
    /// <summary>Full wall-to-wall inventory count.</summary>
    Full = 2,
    /// <summary>Ad-hoc count triggered by a suspected discrepancy.</summary>
    Spot = 3
}

/// <summary>How the locations to count are selected for a plan.</summary>
public enum CountSelectionMode
{
    /// <summary>Operator supplies an explicit location list.</summary>
    ByLocation = 1,
    /// <summary>Every active location in a zone.</summary>
    ByZone = 2,
    /// <summary>Every location currently holding a given item.</summary>
    ByItem = 3,
    /// <summary>Every active, countable location in the warehouse.</summary>
    ByWarehouse = 4
}

/// <summary>Count plan lifecycle, mirroring the inbound order lifecycle shape.</summary>
public enum CountPlanStatus
{
    Draft = 1,
    Released = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}

/// <summary>Count task lifecycle, mirroring the documented pick task lifecycle (§7.4).</summary>
public enum CountTaskStatus
{
    Created = 1,
    Assigned = 2,
    InProgress = 3,
    /// <summary>Counted, quantity matched the system balance - no adjustment raised.</summary>
    Counted = 4,
    /// <summary>Counted, quantity differed - an InventoryAdjustment was raised for approval.</summary>
    VarianceFound = 5,
    Completed = 6,
    Cancelled = 7
}

/// <summary>
/// Adjustment lifecycle. The §12 endpoint /api/inventory-adjustments/{id}/approve
/// implies a pending -> approved gate before stock is touched.
/// </summary>
public enum AdjustmentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>Why a stock adjustment was raised.</summary>
public enum AdjustmentReason
{
    CountVariance = 1,
    Damage = 2,
    Expiry = 3,
    Loss = 4,
    Found = 5,
    SystemCorrection = 6,
    Return = 7
}

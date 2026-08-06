namespace IMS.Domain.Enums;

/// <summary>Doc §7.1 - outbound order lifecycle.</summary>
public enum OrderStatus
{
    Draft = 1,
    Created = 2,
    Released = 3,
    PartiallyAllocated = 4,
    Allocated = 5,
    Picking = 6,
    Picked = 7,
    Packed = 8,
    Shipped = 9,
    Cancelled = 10
}

/// <summary>Doc §7.1 - example order types.</summary>
public enum OrderType
{
    Standard = 1,
    Express = 2,
    Wholesale = 3,
    Retail = 4,
    Transfer = 5,
    ReturnReplacement = 6
}

/// <summary>
/// Doc §7.2 - OrderDetail.Status. Field is named but its states are not enumerated;
/// line-level mirror of the order lifecycle. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public enum OrderDetailStatus
{
    Open = 1,
    PartiallyAllocated = 2,
    Allocated = 3,
    Picking = 4,
    Picked = 5,
    Shipped = 6,
    Cancelled = 7
}

/// <summary>
/// Doc §7.3 - InventoryAllocation.Status. Field is named but its states are not
/// enumerated. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public enum AllocationStatus
{
    Allocated = 1,
    Picked = 2,
    Shipped = 3,
    Cancelled = 4
}

/// <summary>
/// Doc §7.3 - InventoryAllocation.AllocationStrategy. The strategy that selected the
/// source stock. Algorithms are explicitly out of scope for v1 (doc §10), so only
/// Manual and Fifo/Fefo markers are recorded for later analysis.
/// </summary>
public enum AllocationStrategy
{
    Manual = 1,
    Fifo = 2,
    Fefo = 3,
    Lifo = 4
}

/// <summary>Doc §7.4 - pick task lifecycle.</summary>
public enum PickTaskStatus
{
    Created = 1,
    Assigned = 2,
    InProgress = 3,
    Completed = 4,
    ShortPicked = 5,
    Cancelled = 6
}

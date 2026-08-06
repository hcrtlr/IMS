namespace IMS.Domain.Enums;

/// <summary>
/// Doc §5.6 - every stock change writes one of these to the immutable ledger.
/// </summary>
public enum InventoryTransactionType
{
    Receipt = 1,
    Putaway = 2,
    Movement = 3,
    Allocation = 4,
    Deallocation = 5,
    Pick = 6,
    Ship = 7,
    CountAdjustment = 8,
    Damage = 9,
    Return = 10,
    StatusChange = 11
}

/// <summary>
/// Doc §5.6 - InventoryTransaction.ReferenceType, identifying which business
/// document caused the stock change.
/// </summary>
public enum TransactionReferenceType
{
    Manual = 1,
    InboundOrder = 2,
    Receipt = 3,
    PutawayTask = 4,
    Order = 5,
    Allocation = 6,
    PickTask = 7,
    Shipment = 8,
    CountTask = 9,
    InventoryAdjustment = 10,
    Movement = 11
}

/// <summary>Doc §5.3 - Lot.Status.</summary>
public enum LotStatus
{
    Active = 1,
    OnHold = 2,
    Expired = 3,
    Blocked = 4,
    Consumed = 5
}

/// <summary>Doc §5.4 - SerialNumber.Status.</summary>
public enum SerialStatus
{
    Available = 1,
    Allocated = 2,
    Shipped = 3,
    OnHold = 4,
    Damaged = 5,
    Returned = 6
}

/// <summary>Doc §5.5 - pallet / case / tote container kinds.</summary>
public enum LicensePlateType
{
    Pallet = 1,
    Case = 2,
    Tote = 3,
    Carton = 4,
    Cage = 5
}

/// <summary>Doc §5.5 - LicensePlate.Status.</summary>
public enum LicensePlateStatus
{
    Open = 1,
    Closed = 2,
    Shipped = 3,
    Cancelled = 4
}

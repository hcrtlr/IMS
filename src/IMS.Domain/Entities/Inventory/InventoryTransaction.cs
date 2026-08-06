using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inventory;

/// <summary>
/// Doc §5.6 - the immutable ledger. Every stock change must write one of these
/// (rule §11.9) and records must never be updated or deleted (rule §11.10).
///
/// Immutability is enforced in three places: this type exposes init-only properties,
/// the DbContext rejects Modified/Deleted entries for it, and the database has
/// UPDATE/DELETE triggers as a final backstop.
/// </summary>
public class InventoryTransaction : BaseEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>Source location; null for stock appearing from outside (receipt, positive adjustment).</summary>
    public Guid? FromLocationId { get; set; }
    public Location? FromLocation { get; set; }

    /// <summary>Destination location; null for stock leaving the warehouse (ship, negative adjustment).</summary>
    public Guid? ToLocationId { get; set; }
    public Location? ToLocation { get; set; }

    public Guid? FromInventoryStatusId { get; set; }
    public InventoryStatus? FromInventoryStatus { get; set; }

    public Guid? ToInventoryStatusId { get; set; }
    public InventoryStatus? ToInventoryStatus { get; set; }

    public Guid? LotId { get; set; }
    public Lot? Lot { get; set; }

    public Guid? SerialId { get; set; }
    public SerialNumber? Serial { get; set; }

    public Guid? LicensePlateId { get; set; }
    public LicensePlate? LicensePlate { get; set; }

    public InventoryTransactionType TransactionType { get; set; }

    /// <summary>Always positive; direction is conveyed by From/To location and transaction type.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Which kind of business document caused this change.</summary>
    public TransactionReferenceType ReferenceType { get; set; }

    /// <summary>Id of the causing document (receipt, pick task, adjustment, ...).</summary>
    public Guid? ReferenceId { get; set; }

    /// <summary>
    /// Groups every transaction written by one business operation, so a multi-leg
    /// operation (e.g. a movement's decrement + increment) can be traced as a unit.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Username of the actor, taken from the authenticated principal.</summary>
    public string? PerformedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Free-text context, e.g. the adjustment reason or a short-pick note.</summary>
    public string? Notes { get; set; }
}

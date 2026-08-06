namespace IMS.Domain.Enums;

/// <summary>Doc §6.1 - inbound order lifecycle.</summary>
public enum InboundOrderStatus
{
    Draft = 1,
    Expected = 2,
    PartiallyReceived = 3,
    Received = 4,
    Completed = 5,
    Cancelled = 6
}

/// <summary>
/// Doc §6.4 - putaway task lifecycle. The doc names the entity's Status field but lists
/// concrete states only for pick tasks (§7.4); the same lifecycle shape is applied here.
/// ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public enum PutawayTaskStatus
{
    Created = 1,
    Assigned = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}

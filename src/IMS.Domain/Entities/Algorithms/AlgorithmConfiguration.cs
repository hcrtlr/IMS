using IMS.Domain.Common;

namespace IMS.Domain.Entities.Algorithms;

// -----------------------------------------------------------------------------
// Faz 6 "Algorithm Readiness". The document names these tables but specifies no
// fields, and §10 is explicit that algorithms themselves are NOT built in v1.
// These are therefore schema + capture only: nothing reads them to make decisions.
// See docs/ASSUMPTIONS.md.
// -----------------------------------------------------------------------------

/// <summary>
/// Faz 6 "Algorithm configuration tablolari" - named, tunable parameters for the future
/// slotting / putaway / picking / batching engines, so they need no code change to retune.
/// </summary>
public class AlgorithmConfiguration : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }

    /// <summary>Null means the setting applies to every warehouse in the account.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Which engine the setting belongs to, e.g. "Slotting", "Putaway", "Picking", "Batching".</summary>
    public string AlgorithmName { get; set; } = null!;

    /// <summary>Parameter key, e.g. "MaxTravelDistance", "PreferredStrategy".</summary>
    public string ParameterKey { get; set; } = null!;

    /// <summary>Parameter value, stored as text and parsed per ValueType.</summary>
    public string ParameterValue { get; set; } = null!;

    /// <summary>How to interpret ParameterValue: "string", "int", "decimal", "bool", "json".</summary>
    public string ValueType { get; set; } = "string";

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    /// <summary>Ranks competing rules when several match, higher wins.</summary>
    public int Priority { get; set; }
}

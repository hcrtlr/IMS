namespace IMS.Domain.Exceptions;

/// <summary>Base for every rule violation raised by the domain or application layer.</summary>
public abstract class DomainException : Exception
{
    /// <summary>Stable machine-readable code surfaced in the API error payload.</summary>
    public abstract string ErrorCode { get; }

    protected DomainException(string message) : base(message) { }
}

/// <summary>A documented business rule (doc §11) was violated.</summary>
public class BusinessRuleViolationException : DomainException
{
    public override string ErrorCode => "BUSINESS_RULE_VIOLATION";

    /// <summary>The doc §11 rule number, when the violation maps to a numbered rule.</summary>
    public int? RuleNumber { get; }

    public BusinessRuleViolationException(string message, int? ruleNumber = null) : base(message)
        => RuleNumber = ruleNumber;
}

/// <summary>
/// Doc §11.1: "Yeterli available stok yoksa allocation yapilamamalidir."
/// Carries the per-item shortfall required by acceptance scenario 3.
/// </summary>
public class InsufficientStockException : DomainException
{
    public override string ErrorCode => "INSUFFICIENT_STOCK";

    public IReadOnlyList<StockShortfall> Shortfalls { get; }

    public InsufficientStockException(IReadOnlyList<StockShortfall> shortfalls)
        : base(BuildMessage(shortfalls)) => Shortfalls = shortfalls;

    public InsufficientStockException(StockShortfall shortfall)
        : this(new[] { shortfall }) { }

    private static string BuildMessage(IReadOnlyList<StockShortfall> shortfalls)
        => "Insufficient available stock: " + string.Join("; ", shortfalls.Select(s =>
            $"item {s.Sku} requested {s.RequestedQuantity}, available {s.AvailableQuantity}, short {s.ShortQuantity}"));
}

/// <summary>Per-item shortfall detail. Acceptance scenario 3 requires reporting which item is short and by how much.</summary>
public sealed record StockShortfall(
    Guid ItemId,
    string Sku,
    decimal RequestedQuantity,
    decimal AvailableQuantity)
{
    public decimal ShortQuantity => RequestedQuantity - AvailableQuantity;
}

/// <summary>A referenced entity does not exist (or is outside the caller's account scope).</summary>
public class NotFoundException : DomainException
{
    public override string ErrorCode => "NOT_FOUND";

    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}

/// <summary>
/// Doc §11.11: concurrency control is required for simultaneous operations on the same stock.
/// Raised when an optimistic-concurrency token check fails.
/// </summary>
public class ConcurrencyConflictException : DomainException
{
    public override string ErrorCode => "CONCURRENCY_CONFLICT";

    public ConcurrencyConflictException(string message)
        : base(message) { }
}

/// <summary>An entity transition was requested that the status lifecycle does not permit.</summary>
public class InvalidStateTransitionException : DomainException
{
    public override string ErrorCode => "INVALID_STATE_TRANSITION";

    public InvalidStateTransitionException(string entityName, string from, string to)
        : base($"{entityName} cannot transition from '{from}' to '{to}'.") { }
}

/// <summary>A uniqueness constraint defined by the doc (e.g. SKU unique per account, §4.1) was violated.</summary>
public class DuplicateEntityException : DomainException
{
    public override string ErrorCode => "DUPLICATE_ENTITY";

    public DuplicateEntityException(string message) : base(message) { }
}

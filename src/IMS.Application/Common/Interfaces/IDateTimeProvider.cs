namespace IMS.Application.Common.Interfaces;

/// <summary>
/// Abstracts the clock so time-dependent rules (expiration checks, shelf-life filters)
/// stay deterministic under test.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

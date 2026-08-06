using IMS.Application.Common.Interfaces;

namespace IMS.Infrastructure.Services;

/// <summary>System clock. Tests substitute a fixed implementation.</summary>
public class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

using BulkReversal.Application.Common.Interfaces;

namespace BulkReversal.UnitTests.TestSupport;

public class FixedDateTimeProvider : IDateTimeProvider
{
    private readonly DateOnly _today;

    public FixedDateTimeProvider(DateOnly today) => _today = today;

    public DateTimeOffset UtcNow => new(_today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    public DateOnly Today => _today;
}

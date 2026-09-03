namespace BulkReversal.Application.Common.Interfaces.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs <paramref name="operation"/> inside a serializable-ish transaction with retry, for
    /// operations that must be atomic across multiple aggregates (e.g. callback apply + save).</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}

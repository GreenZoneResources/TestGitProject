using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Common.Interfaces.Persistence;

public interface IReversalTransactionRepository
{
    Task<ReversalTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>BRU-04: has this reference already been reversed anywhere in the system?</summary>
    Task<bool> IsAlreadyReversedAsync(string sessionIdOrFtReference, CancellationToken ct = default);

    /// <summary>BRU-04, bulk form: of the given references, which have already been reversed? Avoids
    /// one round-trip per row when validating an up-to-500-row batch.</summary>
    Task<HashSet<string>> GetAlreadyReversedReferencesAsync(IEnumerable<string> sessionIdOrFtReferences, CancellationToken ct = default);

    /// <summary>Fetches the next batch of rows eligible for engine pickup (Status = Submitted or already Processing), oldest first.</summary>
    Task<IReadOnlyList<ReversalTransaction>> GetPendingForEngineAsync(int maxItems, CancellationToken ct = default);

    Task<(IReadOnlyList<ReversalTransaction> Items, int TotalCount)> SearchAsync(
        string? batchReference,
        ReversalStatus? status,
        TransactionType? transactionType,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

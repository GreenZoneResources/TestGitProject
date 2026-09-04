using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Common.Interfaces.Persistence;

public interface IReversalTransactionRepository
{
    Task<ReversalTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>BRU-04: does this reference already have an active reversal elsewhere in the system —
    /// already reversed (<see cref="ReversalStatus.Reversed"/>), already released to the engine
    /// (<see cref="ReversalStatus.Submitted"/>/<see cref="ReversalStatus.Processing"/>), or still
    /// staged as a valid, not-yet-decided row in another batch? A <see cref="ReversalStatus.Rejected"/>
    /// row does NOT count — Settlement may legitimately resubmit after a rejection.
    /// <paramref name="excludeTransactionId"/> excludes the row being edited/retried from matching itself.</summary>
    Task<bool> HasActiveConflictAsync(string sessionIdOrFtReference, Guid? excludeTransactionId, CancellationToken ct = default);

    /// <summary>BRU-04, bulk form: of the given references, which already have an active conflict per
    /// <see cref="HasActiveConflictAsync"/>? Avoids one round-trip per row when validating a batch.
    /// <paramref name="excludeBatchId"/> excludes an entire batch's own rows from matching themselves —
    /// used when re-checking a batch's own valid rows immediately before approval.</summary>
    Task<HashSet<string>> GetActiveConflictReferencesAsync(IEnumerable<string> sessionIdOrFtReferences, Guid? excludeBatchId = null, CancellationToken ct = default);

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

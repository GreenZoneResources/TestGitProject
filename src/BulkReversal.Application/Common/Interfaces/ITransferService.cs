namespace BulkReversal.Application.Common.Interfaces;

public enum TransferReferenceLookupStatus
{
    Found,
    NotFound,

    /// <summary>The check could not be completed (network/timeout/unexpected response) —
    /// distinct from NotFound so callers fail closed rather than treating "couldn't verify" as
    /// "doesn't exist".</summary>
    Unavailable
}

/// <summary>Outcome of looking up a funds-transfer reference against the Transfer Service (BRU-05).</summary>
public record TransferReferenceLookupResult(
    TransferReferenceLookupStatus Status,
    string? AccountNumber,
    decimal? Amount,
    string? ErrorMessage)
{
    public static TransferReferenceLookupResult Found(string? accountNumber, decimal? amount) =>
        new(TransferReferenceLookupStatus.Found, accountNumber, amount, null);

    public static TransferReferenceLookupResult NotFound() =>
        new(TransferReferenceLookupStatus.NotFound, null, null, null);

    public static TransferReferenceLookupResult Unavailable(string errorMessage) =>
        new(TransferReferenceLookupStatus.Unavailable, null, null, errorMessage);
}

/// <summary>
/// Port to the core-banking Transfer Service, used to independently confirm that a submitted
/// Session ID / FT Reference matches a genuine source transaction (BRD BRU-05, FR-12) before a
/// reversal request is accepted.
/// </summary>
public interface ITransferService
{
    Task<TransferReferenceLookupResult> GetTransactionByReferenceAsync(string referenceNumber, CancellationToken ct = default);
}

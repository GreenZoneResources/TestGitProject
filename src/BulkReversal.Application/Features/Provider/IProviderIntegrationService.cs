using BulkReversal.Application.Features.Provider.Dtos;

namespace BulkReversal.Application.Features.Provider;

/// <summary>Result of applying an inbound callback, distinguishing "not our msgId" from a
/// normal (possibly repeated, per §2.5) success/failure application.</summary>
public enum CallbackOutcome
{
    Applied,
    MalformedMsgId,
    UnknownMsgId
}

/// <summary>
/// The two endpoints BulkReversal.API exposes to the reversal engine, per
/// provider-integration-contract.md: GET pending reversals (§1) and POST callback (§2).
/// BulkReversal.API is the "provider" in that contract's terminology.
/// </summary>
public interface IProviderIntegrationService
{
    /// <summary>§1: items the engine has not yet been told the outcome of. Marks each item
    /// "Processing" on first pickup, per FR-15.</summary>
    Task<IReadOnlyList<PendingReversalItemDto>> GetPendingReversalsAsync(int maxItems, CancellationToken ct = default);

    /// <summary>§2: apply the engine's outcome notification. Idempotent for an already-terminal msgId (§2.5).</summary>
    Task<CallbackOutcome> ApplyCallbackAsync(ReversalCallbackRequest request, CancellationToken ct = default);
}

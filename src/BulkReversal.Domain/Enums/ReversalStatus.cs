namespace BulkReversal.Domain.Enums;

/// <summary>
/// Post-approval processing status of an individual reversal transaction, surfaced on the
/// Status Monitoring screen (FR-15): Submitted, Processing, Reversed, or Rejected (with reason).
/// </summary>
public enum ReversalStatus
{
    /// <summary>Approved and staged; not yet picked up by the reversal engine.</summary>
    Submitted = 1,

    /// <summary>Retrieved by the reversal engine via the pending-reversals API and awaiting outcome.</summary>
    Processing = 2,

    /// <summary>Engine revalidation succeeded and accounting entries were posted (FR-13).</summary>
    Reversed = 3,

    /// <summary>Engine revalidation failed, or the request otherwise could not be processed (FR-14).</summary>
    Rejected = 4
}

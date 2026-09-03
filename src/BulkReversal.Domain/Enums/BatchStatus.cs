namespace BulkReversal.Domain.Enums;

/// <summary>Lifecycle of an uploaded reversal batch, per BRD Sections 3.2-3.4.</summary>
public enum BatchStatus
{
    /// <summary>File uploaded, awaiting/undergoing validation.</summary>
    Uploaded = 1,

    /// <summary>Validation complete; batch holds a mix of Valid/Invalid rows and is staged for approval.</summary>
    Validated = 2,

    /// <summary>Submitted by Settlement for authorization (FR-09, Approvals screen).</summary>
    PendingApproval = 3,

    /// <summary>Approved; valid rows are now eligible for pickup by the reversal engine (FR-10).</summary>
    Approved = 4,

    /// <summary>Rejected at approval stage; no rows are exposed to the engine.</summary>
    Rejected = 5,

    /// <summary>All rows in the batch have reached a terminal state (Reversed/Rejected).</summary>
    Completed = 6
}

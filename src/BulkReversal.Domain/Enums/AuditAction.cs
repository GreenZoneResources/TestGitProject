namespace BulkReversal.Domain.Enums;

/// <summary>Reversal lifecycle actions captured in the audit trail (FR-18).</summary>
public enum AuditAction
{
    Upload = 1,
    Validation = 2,
    Submission = 3,
    Approval = 4,
    Rejection = 5,
    ApiRetrieval = 6,
    ProcessingOutcome = 7,
    Exception = 8
}

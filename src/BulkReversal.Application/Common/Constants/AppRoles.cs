namespace BulkReversal.Application.Common.Constants;

/// <summary>
/// Role names expected in the SSO "appRoles" claim for this application (FR-02: RBAC restricted to
/// authorized Settlement users). These are the role names an AD/SSO administrator maps Settlement
/// staff into for the BulkReversal.API application entry in the SSO's app-role registry.
/// </summary>
public static class AppRoles
{
    /// <summary>Can upload batches and view status/dashboard/reports.</summary>
    public const string SettlementUser = "BulkReversal.SettlementUser";

    /// <summary>Can approve/reject batches awaiting sign-off, in addition to SettlementUser actions.</summary>
    public const string SettlementApprover = "BulkReversal.SettlementApprover";

    /// <summary>Full access, including the audit trail.</summary>
    public const string Administrator = "BulkReversal.Administrator";

    /// <summary>Any authenticated Settlement-side role — used where an endpoint just needs "some" access.</summary>
    public const string AnyUser = $"{SettlementUser},{SettlementApprover},{Administrator}";

    public const string ApproverOrAdmin = $"{SettlementApprover},{Administrator}";

    /// <summary>The full set of role names an Administrator may assign via role management —
    /// kept as an array (rather than only the comma-joined strings above) so callers can validate
    /// a single incoming role name against it.</summary>
    public static readonly IReadOnlyCollection<string> Assignable = [SettlementUser, SettlementApprover, Administrator];
}

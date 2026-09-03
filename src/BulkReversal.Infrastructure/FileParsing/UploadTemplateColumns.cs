namespace BulkReversal.Infrastructure.FileParsing;

/// <summary>Canonical Reversal Upload Template headers (BRD Section 6) plus tolerant matching so
/// minor header variations (spacing, punctuation, case) from real-world files still parse.</summary>
internal static class UploadTemplateColumns
{
    public const string SerialNumber = "S/N";
    public const string TransactionType = "Transaction Type";
    public const string SessionIdOrFtReference = "Session ID / FT Reference";
    public const string Rrn = "RRN";
    public const string AccountNumber = "Account Number";
    public const string TransactionDate = "Transaction Date";
    public const string TransactionAmount = "Transaction Amount";
    public const string Channel = "Channel";
    public const string BeneficiaryBank = "Beneficiary Bank";
    public const string Biller = "Biller";
    public const string ReasonForFailure = "Reason for Failure";
    public const string Comments = "Comments";

    public static readonly IReadOnlyList<string> All =
    [
        SerialNumber, TransactionType, SessionIdOrFtReference, Rrn, AccountNumber, TransactionDate,
        TransactionAmount, Channel, BeneficiaryBank, Biller, ReasonForFailure, Comments
    ];

    /// <summary>Case/whitespace/punctuation-insensitive match, e.g. "SessionID/FTReference" ==
    /// "Session ID / FT Reference".</summary>
    public static string Normalize(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

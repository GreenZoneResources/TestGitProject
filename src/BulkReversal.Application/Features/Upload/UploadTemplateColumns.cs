namespace BulkReversal.Application.Features.Upload;

/// <summary>Canonical Reversal Upload Template headers (BRD Section 6) — the single source of
/// truth for both the downloadable reference template (Excel/CSV, FR-05) and the CSV upload
/// parser, so the two can never drift out of column-order sync with each other.</summary>
public static class UploadTemplateColumns
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
}

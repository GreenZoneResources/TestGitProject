using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Upload.Services;

/// <summary>
/// Stateless, single-row field validation per FR-06: mandatory fields, account number format, and
/// type-conditional fields (RRN for Card, Beneficiary Bank for NIP, Biller for Bill Payment). The
/// caller already supplies typed/structured values (no string parsing needed here) — cross-row
/// checks (in-batch duplicates, BRU-06), cross-system checks (already reversed, BRU-04), and the
/// source-transaction check (BRU-05, via the Transfer Service) are handled by the orchestrating
/// upload service, which has repository/HTTP access.
/// </summary>
public class UploadRowFieldValidator
{
    private static readonly HashSet<string> ValidChannels =
        new(StringComparer.OrdinalIgnoreCase) { "NIP", "USSD", "Bill Payment", "Card", "Other" };

    private readonly BusinessRulesOptions _rules;
    private readonly IDateTimeProvider _clock;

    public UploadRowFieldValidator(IOptions<BusinessRulesOptions> rules, IDateTimeProvider clock)
    {
        _rules = rules.Value;
        _clock = clock;
    }

    public List<string> Validate(TransactionRevalidationRequest row)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(row.TransactionType))
            errors.Add("Transaction Type is required and must be NIP, Bill Payment, or Card.");

        if (string.IsNullOrWhiteSpace(row.SessionIdOrFtReference))
            errors.Add("Session ID / FT Reference is required.");

        var accountNumber = row.AccountNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountNumber))
            errors.Add("Account Number is required.");
        else if (!IsValidNubanFormat(accountNumber))
            errors.Add($"Account Number '{accountNumber}' is not a valid 10-digit account number.");

        if (row.TransactionDate == default)
        {
            errors.Add("Transaction Date is required.");
        }
        else
        {
            var today = _clock.Today;
            if (row.TransactionDate > today)
                errors.Add("Transaction Date cannot be in the future.");
            else if (row.TransactionDate < today.AddDays(-_rules.MaxTransactionAgeDays))
                errors.Add($"Transaction Date is older than the {_rules.MaxTransactionAgeDays}-day acceptance window.");
        }

        if (row.TransactionAmount <= 0)
            errors.Add("Transaction Amount must be greater than zero.");

        var channel = row.Channel?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(channel))
            errors.Add("Channel is required.");
        else if (!ValidChannels.Contains(channel))
            errors.Add($"Channel '{channel}' is not recognized; expected NIP, USSD, Bill Payment, Card, or Other.");

        if (string.IsNullOrWhiteSpace(row.ReasonForFailure))
            errors.Add("Reason for Failure is required.");

        if (row.TransactionType == TransactionType.Card && string.IsNullOrWhiteSpace(row.Rrn))
            errors.Add("RRN is required for Card transactions.");

        if (row.TransactionType == TransactionType.Nip && string.IsNullOrWhiteSpace(row.BeneficiaryBank))
            errors.Add("Beneficiary Bank is required for NIP transactions.");

        if (row.TransactionType == TransactionType.BillPayment && string.IsNullOrWhiteSpace(row.Biller))
            errors.Add("Biller is required for Bill Payment transactions.");

        return errors;
    }

    private static bool IsValidNubanFormat(string accountNumber) =>
        accountNumber.Length == 10 && accountNumber.All(char.IsDigit);
}

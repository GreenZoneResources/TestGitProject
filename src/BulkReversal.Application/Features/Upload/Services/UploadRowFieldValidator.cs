using System.Globalization;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace BulkReversal.Application.Features.Upload.Services;

/// <summary>Result of field-level (single-row, no I/O) validation of a raw upload row.</summary>
public record ParsedRow(
    bool IsValid,
    IReadOnlyList<string> Errors,
    TransactionType TransactionType,
    string SessionIdOrFtReference,
    string? Rrn,
    string AccountNumber,
    DateOnly TransactionDate,
    decimal TransactionAmount,
    string Channel,
    string? BeneficiaryBank,
    string? Biller,
    string ReasonForFailure,
    string? Comments);

/// <summary>
/// Stateless, single-row validation per FR-06: file structure/mandatory fields, account number
/// format, and type-conditional fields (RRN for Card, Beneficiary Bank for NIP, Biller for Bill
/// Payment). Cross-row checks (in-file duplicates, BRU-06) and cross-system checks (already
/// reversed, BRU-04) are handled by the orchestrating upload service, which has repository access.
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

    public ParsedRow Validate(RawUploadRow row)
    {
        var errors = new List<string>();

        if (!TryParseTransactionType(row.TransactionType, out var transactionType))
            errors.Add($"Transaction Type '{row.TransactionType}' is invalid; expected NIP, Bill Payment, or Card.");

        var ftReference = row.SessionIdOrFtReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ftReference))
            errors.Add("Session ID / FT Reference is required.");

        var accountNumber = row.AccountNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountNumber))
            errors.Add("Account Number is required.");
        else if (!IsValidNubanFormat(accountNumber))
            errors.Add($"Account Number '{accountNumber}' is not a valid 10-digit account number.");

        DateOnly transactionDate = default;
        if (string.IsNullOrWhiteSpace(row.TransactionDate))
        {
            errors.Add("Transaction Date is required.");
        }
        else if (!TryParseDate(row.TransactionDate, out transactionDate))
        {
            errors.Add($"Transaction Date '{row.TransactionDate}' is not a valid date (expected DD/MM/YYYY).");
        }
        else
        {
            var today = _clock.Today;
            if (transactionDate > today)
                errors.Add("Transaction Date cannot be in the future.");
            else if (transactionDate < today.AddDays(-_rules.MaxTransactionAgeDays))
                errors.Add($"Transaction Date is older than the {_rules.MaxTransactionAgeDays}-day acceptance window.");
        }

        decimal amount = 0;
        if (string.IsNullOrWhiteSpace(row.TransactionAmount))
        {
            errors.Add("Transaction Amount is required.");
        }
        else if (!TryParseAmount(row.TransactionAmount, out amount))
        {
            errors.Add($"Transaction Amount '{row.TransactionAmount}' is not a valid amount.");
        }
        else if (amount <= 0)
        {
            errors.Add("Transaction Amount must be greater than zero.");
        }

        var channel = row.Channel?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(channel))
            errors.Add("Channel is required.");
        else if (!ValidChannels.Contains(channel))
            errors.Add($"Channel '{channel}' is not recognized; expected NIP, USSD, Bill Payment, Card, or Other.");

        var reasonForFailure = row.ReasonForFailure?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reasonForFailure))
            errors.Add("Reason for Failure is required.");

        var rrn = row.Rrn?.Trim();
        var beneficiaryBank = row.BeneficiaryBank?.Trim();
        var biller = row.Biller?.Trim();

        if (transactionType == TransactionType.Card && string.IsNullOrWhiteSpace(rrn))
            errors.Add("RRN is required for Card transactions.");

        if (transactionType == TransactionType.Nip && string.IsNullOrWhiteSpace(beneficiaryBank))
            errors.Add("Beneficiary Bank is required for NIP transactions.");

        if (transactionType == TransactionType.BillPayment && string.IsNullOrWhiteSpace(biller))
            errors.Add("Biller is required for Bill Payment transactions.");

        return new ParsedRow(
            IsValid: errors.Count == 0,
            Errors: errors,
            TransactionType: transactionType,
            SessionIdOrFtReference: ftReference,
            Rrn: rrn,
            AccountNumber: accountNumber,
            TransactionDate: transactionDate,
            TransactionAmount: amount,
            Channel: channel,
            BeneficiaryBank: beneficiaryBank,
            Biller: biller,
            ReasonForFailure: reasonForFailure,
            Comments: row.Comments?.Trim());
    }

    private static bool TryParseTransactionType(string? raw, out TransactionType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var normalized = raw.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        switch (normalized.ToUpperInvariant())
        {
            case "NIP":
                type = TransactionType.Nip;
                return true;
            case "BILLPAYMENT":
            case "BILLPAY":
                type = TransactionType.BillPayment;
                return true;
            case "CARD":
                type = TransactionType.Card;
                return true;
            default:
                return false;
        }
    }

    private static bool IsValidNubanFormat(string accountNumber) =>
        accountNumber.Length == 10 && accountNumber.All(char.IsDigit);

    private static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];
        if (DateOnly.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        return DateOnly.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseAmount(string? raw, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var cleaned = raw.Trim().Replace(",", "", StringComparison.Ordinal).Replace("₦", "", StringComparison.Ordinal).Replace("NGN", "", StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}

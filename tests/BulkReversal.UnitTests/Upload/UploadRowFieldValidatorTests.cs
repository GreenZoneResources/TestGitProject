using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Application.Features.Upload.Services;
using BulkReversal.Domain.Enums;
using BulkReversal.UnitTests.TestSupport;
using Microsoft.Extensions.Options;
using Xunit;

namespace BulkReversal.UnitTests.Upload;

public class UploadRowFieldValidatorTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private readonly UploadRowFieldValidator _validator = new(
        Options.Create(new BusinessRulesOptions { MaxTransactionAgeDays = 365 }),
        new FixedDateTimeProvider(Today));

    private static TransactionRevalidationRequest ValidNipRow() => new()
    {
        TransactionType = TransactionType.Nip,
        SessionIdOrFtReference = "FT0000000001",
        AccountNumber = "0123456789",
        TransactionDate = new DateOnly(2026, 8, 1),
        TransactionAmount = 50_000m,
        Channel = "NIP",
        BeneficiaryBank = "GTBank",
        ReasonForFailure = "No value received by beneficiary"
    };

    [Fact]
    public void Validate_WithAllRequiredFields_ReturnsNoErrors()
    {
        var errors = _validator.Validate(ValidNipRow());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingTransactionType_ReturnsError()
    {
        var row = ValidNipRow();
        row.TransactionType = default; // not sent by the client

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Transaction Type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingSessionIdOrFtReference_ReturnsError()
    {
        var row = ValidNipRow();
        row.SessionIdOrFtReference = "";

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Session ID", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("ABCDEFGHIJ")]
    [InlineData("01234567890")]
    public void Validate_InvalidAccountNumberFormat_ReturnsError(string accountNumber)
    {
        var row = ValidNipRow();
        row.AccountNumber = accountNumber;

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Account Number", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FutureTransactionDate_ReturnsError()
    {
        var row = ValidNipRow();
        row.TransactionDate = Today.AddDays(1);

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("cannot be in the future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TransactionDateOlderThanAcceptanceWindow_ReturnsError()
    {
        var row = ValidNipRow();
        row.TransactionDate = Today.AddDays(-400);

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("acceptance window", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveAmount_ReturnsError(decimal amount)
    {
        var row = ValidNipRow();
        row.TransactionAmount = amount;

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("greater than zero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UnrecognizedChannel_ReturnsError()
    {
        var row = ValidNipRow();
        row.Channel = "Carrier Pigeon";

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Channel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingReasonForFailure_ReturnsError()
    {
        var row = ValidNipRow();
        row.ReasonForFailure = "";

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Reason for Failure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CardWithoutRrn_ReturnsError()
    {
        var row = ValidNipRow();
        row.TransactionType = TransactionType.Card;
        row.Rrn = null;
        row.BeneficiaryBank = null;

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("RRN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CardWithRrn_DoesNotRequireBeneficiaryBank()
    {
        var row = ValidNipRow();
        row.TransactionType = TransactionType.Card;
        row.Rrn = "534871234567";
        row.BeneficiaryBank = null;

        var errors = _validator.Validate(row);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NipWithoutBeneficiaryBank_ReturnsError()
    {
        var row = ValidNipRow();
        row.BeneficiaryBank = null;

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Beneficiary Bank", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_BillPaymentWithoutBiller_ReturnsError()
    {
        var row = ValidNipRow();
        row.TransactionType = TransactionType.BillPayment;
        row.Channel = "Bill Payment";
        row.BeneficiaryBank = null;
        row.Biller = null;

        var errors = _validator.Validate(row);

        Assert.Contains(errors, e => e.Contains("Biller", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MultipleMissingFields_ReturnsOneErrorPerIssue()
    {
        var row = new TransactionRevalidationRequest();

        var errors = _validator.Validate(row);

        // Transaction Type, FT Reference, Account Number, Transaction Date, Amount, Channel, Reason.
        Assert.True(errors.Count >= 7, $"Expected at least 7 errors, got {errors.Count}: {string.Join(" | ", errors)}");
    }
}

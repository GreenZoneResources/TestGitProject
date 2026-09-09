using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Features.Upload.Services;
using BulkReversal.Domain.Enums;
using Xunit;

namespace BulkReversal.UnitTests.Upload;

public class CsvBatchRequestParserTests
{
    private const string Header = "S/N,Transaction Type,Session ID / FT Reference,RRN,Account Number,Transaction Date,Transaction Amount,Channel,Beneficiary Bank,Biller,Reason for Failure,Comments";

    [Fact]
    public void Parse_WellFormedCsv_ProducesMatchingTransactions()
    {
        var csv = Header + "\n" +
            "1,NIP,FT0000000001,,0123456789,01/08/2026,50000.00,NIP,GTBank,,No value received,\n";

        var result = CsvBatchRequestParser.Parse("CSV Batch", csv);

        Assert.Equal("CSV Batch", result.BatchName);
        var row = Assert.Single(result.Transactions);
        Assert.Equal(TransactionType.Nip, row.TransactionType);
        Assert.Equal("FT0000000001", row.SessionIdOrFtReference);
        Assert.Equal("0123456789", row.AccountNumber);
        Assert.Equal(new DateOnly(2026, 8, 1), row.TransactionDate);
        Assert.Equal(50_000m, row.TransactionAmount);
        Assert.Equal("NIP", row.Channel);
        Assert.Equal("GTBank", row.BeneficiaryBank);
        Assert.Null(row.Biller);
    }

    [Fact]
    public void Parse_ColumnsReordered_StillMatchesByHeaderName()
    {
        var csv = "Account Number,Transaction Type,Session ID / FT Reference,Transaction Date,Transaction Amount,Channel,Reason for Failure\n" +
            "0123456789,Card,FT0000000002,2026-08-01,15000,Card,Chargeback\n";

        var result = CsvBatchRequestParser.Parse("Reordered", csv);

        var row = Assert.Single(result.Transactions);
        Assert.Equal(TransactionType.Card, row.TransactionType);
        Assert.Equal(15_000m, row.TransactionAmount);
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedComma_ParsesCorrectly()
    {
        var csv = Header + "\n" +
            "1,NIP,FT0000000003,,0123456789,01/08/2026,1000,NIP,GTBank,,\"Reason, with a comma\",\"Comment, too\"\n";

        var result = CsvBatchRequestParser.Parse("Quoted", csv);

        var row = Assert.Single(result.Transactions);
        Assert.Equal("Reason, with a comma", row.ReasonForFailure);
        Assert.Equal("Comment, too", row.Comments);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ThrowsValidationAppException()
    {
        var csv = "Session ID / FT Reference,Account Number\nFT01,0123456789\n";

        var ex = Assert.Throws<ValidationAppException>(() => CsvBatchRequestParser.Parse("Missing Cols", csv));
        Assert.Contains("file", ex.Errors.Keys);
    }

    [Fact]
    public void Parse_EmptyContent_ThrowsValidationAppException()
    {
        Assert.Throws<ValidationAppException>(() => CsvBatchRequestParser.Parse("Empty", ""));
    }

    [Fact]
    public void Parse_UnparsableAmount_DefaultsToZeroSoRowValidationCatchesIt()
    {
        var csv = Header + "\n" +
            "1,NIP,FT0000000004,,0123456789,01/08/2026,not-a-number,NIP,GTBank,,Reason,\n";

        var result = CsvBatchRequestParser.Parse("Bad Amount", csv);

        Assert.Equal(0m, Assert.Single(result.Transactions).TransactionAmount);
    }

    [Fact]
    public void Parse_TrailingBlankLine_IsIgnored()
    {
        var csv = Header + "\n" +
            "1,NIP,FT0000000005,,0123456789,01/08/2026,1000,NIP,GTBank,,Reason,\n\n";

        var result = CsvBatchRequestParser.Parse("Trailing Blank", csv);

        Assert.Single(result.Transactions);
    }
}

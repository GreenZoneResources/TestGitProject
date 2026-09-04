using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using BulkReversal.Domain.Exceptions;
using Xunit;

namespace BulkReversal.UnitTests.Domain;

public class ReversalTransactionTests
{
    private static ReversalTransaction CreateSubmittedRow()
    {
        var row = ReversalTransaction.Create(
            Guid.NewGuid(), "BR-TEST-01", 1, TransactionType.Nip, "FT001", null,
            "0123456789", new DateOnly(2026, 8, 1), 1000m, "NIP", "GTBank", null, "reason", null, "user1");
        row.MarkValid();
        row.Submit();
        return row;
    }

    [Fact]
    public void ApplyCallback_Success_SetsReversedStatusAndExternalReference()
    {
        var row = CreateSubmittedRow();

        row.ApplyCallback(true, "T24-REF-001", null, null);

        Assert.Equal(ReversalStatus.Reversed, row.Status);
        Assert.Equal("T24-REF-001", row.ExternalReference);
        Assert.Null(row.FailureReason);
    }

    [Fact]
    public void ApplyCallback_Failure_SetsRejectedStatusAndFailureDetails()
    {
        var row = CreateSubmittedRow();

        row.ApplyCallback(false, null, "OFS_REVERSAL_FAILED", "T24 rejected the reversal.");

        Assert.Equal(ReversalStatus.Rejected, row.Status);
        Assert.Equal("OFS_REVERSAL_FAILED", row.FailureCode);
        Assert.Equal("T24 rejected the reversal.", row.FailureReason);
    }

    [Fact]
    public void ApplyCallback_RepeatedForAlreadyTerminalMsgId_IsANoOp()
    {
        var row = CreateSubmittedRow();
        row.ApplyCallback(true, "T24-REF-001", null, null);

        // Provider contract §2.5: a retried delivery for an already-terminal msgId must be safe.
        row.ApplyCallback(false, null, "SHOULD_BE_IGNORED", "should not overwrite");

        Assert.Equal(ReversalStatus.Reversed, row.Status);
        Assert.Equal("T24-REF-001", row.ExternalReference);
        Assert.Null(row.FailureCode);
    }

    [Fact]
    public void EditFields_AfterSubmission_Throws()
    {
        var row = CreateSubmittedRow();

        Assert.Throws<DomainException>(() => row.EditFields(
            TransactionType.Nip, "FT001", null, "0129999999", new DateOnly(2026, 8, 1), 1000m,
            "NIP", "GTBank", null, "reason", null, "user1"));
    }

    [Fact]
    public void MsgId_IsStableAndParsesBackToTheSameId()
    {
        var row = CreateSubmittedRow();

        var parsed = Guid.ParseExact(row.MsgId, "N");

        Assert.Equal(row.Id, parsed);
    }
}

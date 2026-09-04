using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using BulkReversal.Domain.Exceptions;
using Xunit;

namespace BulkReversal.UnitTests.Domain;

public class ReversalBatchTests
{
    private static (ReversalBatch Batch, ReversalTransaction Row) CreateStagedBatchWithOneValidRow()
    {
        var batch = ReversalBatch.Create("Batch", "user1", "User One", "BR-TEST-01");
        var row = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 1, TransactionType.Nip, "FT001", null,
            "0123456789", new DateOnly(2026, 8, 1), 1000m, "NIP", "GTBank", null, "reason", null, "user1");
        row.MarkValid();
        batch.AddTransaction(row);
        batch.CompleteValidation();
        return (batch, row);
    }

    [Fact]
    public void GetMutableTransaction_WhileValidated_Succeeds()
    {
        var (batch, row) = CreateStagedBatchWithOneValidRow();

        var found = batch.GetMutableTransaction(row.Id);

        Assert.Same(row, found);
    }

    [Fact]
    public void GetMutableTransaction_AfterSubmittedForApproval_Throws()
    {
        var (batch, row) = CreateStagedBatchWithOneValidRow();
        batch.SubmitForApproval("approver", "Approver");

        Assert.Throws<DomainException>(() => batch.GetMutableTransaction(row.Id));
    }

    [Fact]
    public void GetMutableTransaction_AfterApproval_Throws()
    {
        var (batch, row) = CreateStagedBatchWithOneValidRow();
        batch.SubmitForApproval("approver", "Approver");
        batch.Approve("approver", "Approver");

        Assert.Throws<DomainException>(() => batch.GetMutableTransaction(row.Id));
    }

    [Fact]
    public void RemoveTransaction_RecalculatesCounts()
    {
        var (batch, row) = CreateStagedBatchWithOneValidRow();

        batch.RemoveTransaction(row.Id);

        Assert.Equal(0, batch.TotalRecords);
        Assert.Equal(0, batch.ValidRecords);
    }

    [Fact]
    public void SubmitForApproval_WithNoValidRecords_Throws()
    {
        var batch = ReversalBatch.Create("Empty", "user1", "User One", "BR-TEST-02");
        var row = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 1, TransactionType.Nip, "FT001", null,
            "bad", new DateOnly(2026, 8, 1), 1000m, "NIP", "GTBank", null, "reason", null, "user1");
        row.MarkInvalid(["bad account"]);
        batch.AddTransaction(row);
        batch.CompleteValidation();

        Assert.Throws<DomainException>(() => batch.SubmitForApproval("user1", "User One"));
    }

    [Fact]
    public void Approve_ReleasesOnlyValidRowsToTheEngine()
    {
        var batch = ReversalBatch.Create("Mixed", "user1", "User One", "BR-TEST-03");

        var valid = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 1, TransactionType.Nip, "FT001", null,
            "0123456789", new DateOnly(2026, 8, 1), 1000m, "NIP", "GTBank", null, "reason", null, "user1");
        valid.MarkValid();
        batch.AddTransaction(valid);

        var invalid = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 2, TransactionType.Nip, "FT002", null,
            "bad", new DateOnly(2026, 8, 1), 1000m, "NIP", "GTBank", null, "reason", null, "user1");
        invalid.MarkInvalid(["bad account"]);
        batch.AddTransaction(invalid);

        batch.CompleteValidation();
        batch.SubmitForApproval("user1", "User One");
        batch.Approve("approver", "Approver");

        Assert.Equal(ReversalStatus.Submitted, valid.Status);
        Assert.Null(invalid.Status);
    }
}

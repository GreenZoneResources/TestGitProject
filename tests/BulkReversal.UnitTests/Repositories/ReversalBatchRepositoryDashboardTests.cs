using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using BulkReversal.Infrastructure.Persistence;
using BulkReversal.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BulkReversal.UnitTests.Repositories;

public class ReversalBatchRepositoryDashboardTests
{
    private static BulkReversalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BulkReversalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Builds an approved batch with one Submitted row, then back-dates DecidedAt directly via the
    // change tracker (ReversalBatch.Approve() always stamps DateTimeOffset.UtcNow, so this is the
    // only way to simulate "approved last month" without adding a time-provider dependency to the
    // aggregate just for this test).
    private static async Task<ReversalBatch> SeedApprovedBatchAsync(BulkReversalDbContext db, DateTimeOffset decidedAt, string reference)
    {
        var batch = ReversalBatch.Create("Batch " + reference, "settlement1", "Settlement User", reference);
        var row = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 1, TransactionType.Nip, "FT-" + reference, null,
            "0123456789", DateOnly.FromDateTime(decidedAt.Date), 1000m, "NIP", "GTBank", null, "reason", null, "settlement1");
        row.MarkValid();
        batch.AddTransaction(row);
        batch.CompleteValidation();
        batch.SubmitForApproval("settlement1", "Settlement User");
        batch.Approve("approver1", "Jane Approver");

        db.ReversalBatches.Add(batch);
        await db.SaveChangesAsync();

        db.Entry(batch).Property("DecidedAt").CurrentValue = decidedAt;
        await db.SaveChangesAsync();

        return batch;
    }

    [Fact]
    public async Task GetDashboardCountsAsync_OnlyCountsSubmittedRowsApprovedThisCalendarMonth()
    {
        using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var thisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        var lastMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-1);

        await SeedApprovedBatchAsync(db, thisMonth, "BR-THIS-01");
        await SeedApprovedBatchAsync(db, thisMonth, "BR-THIS-02");
        await SeedApprovedBatchAsync(db, lastMonth, "BR-LAST-01");

        var sut = new ReversalBatchRepository(db);
        var counts = await sut.GetDashboardCountsAsync();

        Assert.Equal(2, counts.Submitted);
    }

    [Fact]
    public async Task GetDashboardCountsAsync_ProcessingReversedRejected_AreLiveSnapshotsNotMonthScoped()
    {
        using var db = CreateContext();
        var longAgo = DateTimeOffset.UtcNow.AddMonths(-6);

        var processingBatch = await SeedApprovedBatchAsync(db, longAgo, "BR-PROC-01");
        processingBatch.Transactions.Single().MarkRetrievedByEngine();

        var reversedBatch = await SeedApprovedBatchAsync(db, longAgo, "BR-REV-01");
        reversedBatch.Transactions.Single().MarkRetrievedByEngine();
        reversedBatch.Transactions.Single().ApplyCallback(true, "EXT-1", null, null);

        var rejectedBatch = await SeedApprovedBatchAsync(db, longAgo, "BR-REJ-01");
        rejectedBatch.Transactions.Single().MarkRetrievedByEngine();
        rejectedBatch.Transactions.Single().ApplyCallback(false, null, "CODE", "reason");

        await db.SaveChangesAsync();

        var sut = new ReversalBatchRepository(db);
        var counts = await sut.GetDashboardCountsAsync();

        Assert.Equal(1, counts.PendingProcessing);
        Assert.Equal(1, counts.Reversed);
        Assert.Equal(1, counts.RejectedNeedsReview);
    }
}

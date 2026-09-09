using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Approvals;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BulkReversal.UnitTests.Upload;

public class ApprovalServiceTests
{
    private readonly Mock<IReversalBatchRepository> _batchRepository = new();
    private readonly Mock<IReversalTransactionRepository> _transactionRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<IUserContactDirectoryRepository> _contactDirectory = new();
    private readonly Mock<IEmailService> _emailService = new();

    public ApprovalServiceTests()
    {
        _currentUser.Setup(u => u.UserId).Returns("approver1");
        _currentUser.Setup(u => u.UserName).Returns("Jane Approver");
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _contactDirectory
            .Setup(r => r.GetEmailForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("initiator@bank.local");
    }

    private ApprovalService CreateSut() => new(
        _batchRepository.Object, _transactionRepository.Object, _unitOfWork.Object,
        _currentUser.Object, _auditService.Object, _contactDirectory.Object, _emailService.Object,
        Options.Create(new EmailOptions()), NullLogger<ApprovalService>.Instance);

    private static ReversalBatch BuildPendingApprovalBatch(out ReversalTransaction row, string reference = "FT0000000001")
    {
        var batch = ReversalBatch.Create("Batch", "settlement1", "Settlement User", "BR-TEST-01");
        row = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 1, TransactionType.Nip, reference, null,
            "0123456789", new DateOnly(2026, 8, 1), 50_000m, "NIP", "GTBank", null, "reason", null, "settlement1");
        row.MarkValid();
        batch.AddTransaction(row);
        batch.CompleteValidation();
        batch.SubmitForApproval("settlement1", "Settlement User");
        return batch;
    }

    [Fact]
    public async Task ApproveAsync_NoConflict_ApprovesAndReleasesRowsToTheEngine()
    {
        var batch = BuildPendingApprovalBatch(out var row);
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);
        _transactionRepository
            .Setup(r => r.GetActiveConflictReferencesAsync(It.IsAny<IEnumerable<string>>(), batch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        var sut = CreateSut();
        await sut.ApproveAsync(batch.BatchReference);

        Assert.Equal(BatchStatus.Approved, batch.Status);
        Assert.Equal(ReversalStatus.Submitted, row.Status);
    }

    [Fact]
    public async Task ApproveAsync_ReferenceBecameActiveInAnotherBatchSinceStaging_ThrowsConflictAndDoesNotApprove()
    {
        var batch = BuildPendingApprovalBatch(out _, reference: "FT0000000001");
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);
        _transactionRepository
            .Setup(r => r.GetActiveConflictReferencesAsync(It.IsAny<IEnumerable<string>>(), batch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "FT0000000001" });

        var sut = CreateSut();

        await Assert.ThrowsAsync<ConflictAppException>(() => sut.ApproveAsync(batch.BatchReference));

        // The batch must stay PendingApproval — never partially approved.
        Assert.Equal(BatchStatus.PendingApproval, batch.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_ExcludesTheBatchsOwnRowsFromConflictCheck()
    {
        // A batch's own valid row must never be reported as conflicting with itself.
        var batch = BuildPendingApprovalBatch(out _);
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);

        Guid? capturedExcludeBatchId = null;
        _transactionRepository
            .Setup(r => r.GetActiveConflictReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, Guid?, CancellationToken>((_, excludeBatchId, _) => capturedExcludeBatchId = excludeBatchId)
            .ReturnsAsync(new HashSet<string>());

        var sut = CreateSut();
        await sut.ApproveAsync(batch.BatchReference);

        Assert.Equal(batch.Id, capturedExcludeBatchId);
    }
}

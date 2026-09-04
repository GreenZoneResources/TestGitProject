using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Application.Features.Upload.Services;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using BulkReversal.Domain.Exceptions;
using BulkReversal.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BulkReversal.UnitTests.Upload;

public class BatchUploadServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private readonly Mock<IReversalBatchRepository> _batchRepository = new();
    private readonly Mock<IReversalTransactionRepository> _transactionRepository = new();
    private readonly Mock<IBatchReferenceGenerator> _referenceGenerator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<ITransferService> _transferService = new();

    private BusinessRulesOptions _rules = new() { MaxRecordsPerFile = 500, MaxTransactionAgeDays = 365 };
    private TransferServiceOptions _transferOptions = new() { Enabled = false };

    public BatchUploadServiceTests()
    {
        _referenceGenerator.Setup(g => g.NextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("BR-20260815-01");
        _currentUser.Setup(u => u.UserId).Returns("settlement.user1");
        _currentUser.Setup(u => u.UserName).Returns("Settlement User");
        _transactionRepository
            .Setup(r => r.GetActiveConflictReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _transactionRepository
            .Setup(r => r.HasActiveConflictAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private BatchUploadService CreateSut() => new(
        new UploadRowFieldValidator(Options.Create(_rules), new FixedDateTimeProvider(Today)),
        _batchRepository.Object,
        _transactionRepository.Object,
        _referenceGenerator.Object,
        _unitOfWork.Object,
        _currentUser.Object,
        _auditService.Object,
        _transferService.Object,
        Options.Create(_rules),
        Options.Create(_transferOptions),
        NullLogger<BatchUploadService>.Instance);

    private static TransactionRevalidationRequest ValidRow(string reference = "FT0000000001") => new()
    {
        TransactionType = TransactionType.Nip,
        SessionIdOrFtReference = reference,
        AccountNumber = "0123456789",
        TransactionDate = new DateOnly(2026, 8, 1),
        TransactionAmount = 50_000m,
        Channel = "NIP",
        BeneficiaryBank = "GTBank",
        ReasonForFailure = "No value received by beneficiary"
    };

    // --- CreateBatchAsync ---

    [Fact]
    public async Task CreateBatchAsync_WithOneValidRow_StagesValidatedBatch()
    {
        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Test Batch", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(BatchStatus.Validated, result.Status);
        Assert.Equal(1, result.TotalRecords);
        Assert.Equal(1, result.ValidRecords);
        Assert.Equal(0, result.InvalidRecords);
        Assert.Empty(result.InvalidRows);
        _batchRepository.Verify(r => r.Add(It.IsAny<ReversalBatch>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateBatchAsync_ExceedingMaxRecordsPerFile_ThrowsValidationAppException()
    {
        _rules = new BusinessRulesOptions { MaxRecordsPerFile = 1, MaxTransactionAgeDays = 365 };
        var sut = CreateSut();
        var request = new CreateReversalBatchRequest
        {
            BatchName = "Too Big",
            Transactions = { ValidRow("FT0000000001"), ValidRow("FT0000000002") }
        };

        await Assert.ThrowsAsync<ValidationAppException>(() => sut.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatchAsync_DuplicateReferenceWithinBatch_MarksBothRowsInvalid()
    {
        var sut = CreateSut();
        var request = new CreateReversalBatchRequest
        {
            BatchName = "Dup Batch",
            Transactions = { ValidRow("FT0000000001"), ValidRow("FT0000000001") }
        };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(0, result.ValidRecords);
        Assert.Equal(2, result.InvalidRecords);
        Assert.All(result.InvalidRows, row => Assert.Contains(row.Errors, e => e.Contains("more than once", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task CreateBatchAsync_ReferenceAlreadyReversed_MarksRowInvalid()
    {
        _transactionRepository
            .Setup(r => r.GetActiveConflictReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FT0000000001" });

        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Already Reversed", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(0, result.ValidRecords);
        Assert.Contains(result.InvalidRows.Single().Errors, e => e.Contains("already reversed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateBatchAsync_ReferenceActiveInAnotherStagedBatch_MarksRowInvalid()
    {
        // BRU-04 must also catch a reference that's merely staged-and-valid in another batch, not
        // just one that has already fully completed reversal.
        _transactionRepository
            .Setup(r => r.GetActiveConflictReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FT0000000001" });

        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Active Elsewhere", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(0, result.ValidRecords);
        Assert.Contains(result.InvalidRows.Single().Errors, e => e.Contains("active in another batch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateBatchAsync_TransferServiceDisabled_SkipsSourceCheckEntirely()
    {
        _transferOptions = new TransferServiceOptions { Enabled = false };
        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Disabled Check", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(1, result.ValidRecords);
        _transferService.Verify(t => t.GetTransactionByReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBatchAsync_TransferServiceReportsNotFound_MarksRowInvalid()
    {
        _transferOptions = new TransferServiceOptions { Enabled = true, MaxConcurrentRequests = 4 };
        _transferService
            .Setup(t => t.GetTransactionByReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransferReferenceLookupResult.NotFound());

        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Not Found", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(0, result.ValidRecords);
        Assert.Contains(result.InvalidRows.Single().Errors, e => e.Contains("does not match any source transaction record", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateBatchAsync_TransferServiceUnavailable_MarksRowInvalidWithRetryHint()
    {
        _transferOptions = new TransferServiceOptions { Enabled = true, MaxConcurrentRequests = 4 };
        _transferService
            .Setup(t => t.GetTransactionByReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransferReferenceLookupResult.Unavailable("timed out"));

        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Unavailable", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(0, result.ValidRecords);
        Assert.Contains(result.InvalidRows.Single().Errors, e => e.Contains("Unable to verify", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateBatchAsync_TransferServiceFoundWithMismatchedAccount_MarksRowInvalid()
    {
        _transferOptions = new TransferServiceOptions { Enabled = true, MaxConcurrentRequests = 4 };
        _transferService
            .Setup(t => t.GetTransactionByReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransferReferenceLookupResult.Found("9999999999", null));

        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Mismatch", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(0, result.ValidRecords);
        Assert.Contains(result.InvalidRows.Single().Errors, e => e.Contains("does not match the source transaction record", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateBatchAsync_TransferServiceFoundMatching_RowStaysValid()
    {
        _transferOptions = new TransferServiceOptions { Enabled = true, MaxConcurrentRequests = 4 };
        _transferService
            .Setup(t => t.GetTransactionByReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransferReferenceLookupResult.Found("0123456789", 50_000m));

        var sut = CreateSut();
        var request = new CreateReversalBatchRequest { BatchName = "Match", Transactions = { ValidRow() } };

        var result = await sut.CreateBatchAsync(request);

        Assert.Equal(1, result.ValidRecords);
    }

    // --- Edit / Delete / Retry / Submit (against a pre-staged batch) ---

    private ReversalBatch BuildStagedBatch(out ReversalTransaction validRow, out ReversalTransaction invalidRow)
    {
        var batch = ReversalBatch.Create("Existing Batch", "settlement.user1", "Settlement User", "BR-20260815-01");

        validRow = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 1, TransactionType.Nip, "FT0000000001", null,
            "0123456789", new DateOnly(2026, 8, 1), 50_000m, "NIP", "GTBank", null,
            "No value received", null, "settlement.user1");
        validRow.MarkValid();
        batch.AddTransaction(validRow);

        invalidRow = ReversalTransaction.Create(
            batch.Id, batch.BatchReference, 2, TransactionType.Nip, "FT0000000002", null,
            "BADACCT", new DateOnly(2026, 8, 1), 25_000m, "NIP", "GTBank", null,
            "No value received", null, "settlement.user1");
        invalidRow.MarkInvalid(["Account Number 'BADACCT' is not a valid 10-digit account number."]);
        batch.AddTransaction(invalidRow);

        batch.CompleteValidation();
        return batch;
    }

    [Fact]
    public async Task EditRecordAsync_FixingAnInvalidRow_MakesItValid()
    {
        var batch = BuildStagedBatch(out _, out var invalidRow);
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);

        var sut = CreateSut();
        var result = await sut.EditRecordAsync(batch.BatchReference, invalidRow.Id, new EditTransactionRowRequest(AccountNumber: "0129999999"));

        Assert.Equal(2, result.ValidRecords);
        Assert.Equal(0, result.InvalidRecords);
    }

    [Fact]
    public async Task EditRecordAsync_OnSubmittedBatch_ThrowsDomainException()
    {
        var batch = BuildStagedBatch(out var validRow, out _);
        batch.SubmitForApproval("approver", "Approver"); // Validated -> PendingApproval
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);

        var sut = CreateSut();

        await Assert.ThrowsAsync<DomainException>(() =>
            sut.EditRecordAsync(batch.BatchReference, validRow.Id, new EditTransactionRowRequest(AccountNumber: "0129999998")));
    }

    [Fact]
    public async Task DeleteRecordAsync_RemovesRowAndRecalculatesCounts()
    {
        var batch = BuildStagedBatch(out _, out var invalidRow);
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);

        var sut = CreateSut();
        var result = await sut.DeleteRecordAsync(batch.BatchReference, invalidRow.Id);

        Assert.Equal(1, result.TotalRecords);
        Assert.Equal(1, result.ValidRecords);
        Assert.Equal(0, result.InvalidRecords);
    }

    [Fact]
    public async Task RetryValidationAsync_ReevaluatesCurrentValuesWithoutChangingThem()
    {
        var batch = BuildStagedBatch(out _, out var invalidRow);
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);

        var sut = CreateSut();
        var result = await sut.RetryValidationAsync(batch.BatchReference, invalidRow.Id);

        // Still invalid — nothing was fixed, retry just re-checks.
        Assert.Equal(1, result.InvalidRecords);
        Assert.Equal("BADACCT", invalidRow.AccountNumber);
    }

    [Fact]
    public async Task SubmitForReviewAsync_TransitionsValidatedBatchToPendingApproval()
    {
        var batch = BuildStagedBatch(out _, out _);
        _batchRepository.Setup(r => r.GetByReferenceAsync(batch.BatchReference, true, It.IsAny<CancellationToken>())).ReturnsAsync(batch);

        var sut = CreateSut();
        var result = await sut.SubmitForReviewAsync(batch.BatchReference);

        Assert.Equal(BatchStatus.PendingApproval, result.Status);
    }

    [Fact]
    public async Task GetResultAsync_UnknownBatchReference_ThrowsNotFoundAppException()
    {
        _batchRepository
            .Setup(r => r.GetByReferenceAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReversalBatch?)null);

        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundAppException>(() => sut.GetResultAsync("BR-DOES-NOT-EXIST"));
    }
}

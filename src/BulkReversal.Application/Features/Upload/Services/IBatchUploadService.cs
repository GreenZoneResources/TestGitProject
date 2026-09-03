using BulkReversal.Application.Features.Upload.Dtos;

namespace BulkReversal.Application.Features.Upload.Services;

public interface IBatchUploadService
{
    Task<UploadBatchResultDto> UploadAsync(UploadBatchCommand command, CancellationToken ct = default);

    /// <summary>Reconstructs the upload result (counts + invalid-row detail) for an already-staged
    /// batch, e.g. to regenerate the FR-08 error report without re-uploading.</summary>
    Task<UploadBatchResultDto> GetResultAsync(string batchReference, CancellationToken ct = default);

    /// <summary>All rows of a staged batch, for the Upload results screen's review table.</summary>
    Task<IReadOnlyList<TransactionRowDto>> GetRecordsAsync(string batchReference, CancellationToken ct = default);

    /// <summary>Corrects a staged row and revalidates it in place (field rules, in-batch duplicates,
    /// and already-reversed check). Only permitted before the batch is submitted for review.</summary>
    Task<UploadBatchResultDto> EditRecordAsync(string batchReference, Guid transactionId, EditTransactionRowRequest request, CancellationToken ct = default);

    /// <summary>Removes a row from a staged batch. Only permitted before the batch is submitted for review.</summary>
    Task<UploadBatchResultDto> DeleteRecordAsync(string batchReference, Guid transactionId, CancellationToken ct = default);

    /// <summary>Re-runs validation on a row's current values without changing them — useful after a
    /// conflicting row was deleted/edited elsewhere in the batch, or simply to re-check.</summary>
    Task<UploadBatchResultDto> RetryValidationAsync(string batchReference, Guid transactionId, CancellationToken ct = default);

    /// <summary>Explicit "Submit Records For Review" action: moves a Validated batch with at least
    /// one valid row into the Approvals queue (FR-09).</summary>
    Task<UploadBatchResultDto> SubmitForReviewAsync(string batchReference, CancellationToken ct = default);
}

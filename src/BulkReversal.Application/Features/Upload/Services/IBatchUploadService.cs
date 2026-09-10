using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Features.Upload.Dtos;

namespace BulkReversal.Application.Features.Upload.Services;

public interface IBatchUploadService
{
    /// <summary>Validates every submitted row (field rules, BRU-04/05/06) and stages the batch
    /// (FR-03/FR-06/FR-07/FR-09). Stays Validated (editable) until <see cref="SubmitForReviewAsync"/>.</summary>
    Task<UploadBatchResultDto> CreateBatchAsync(CreateReversalBatchRequest request, CancellationToken ct = default);

    /// <summary>Reconstructs the result summary (counts + invalid-row detail) for an already-staged
    /// batch, e.g. to regenerate the FR-08 error report.</summary>
    Task<UploadBatchResultDto> GetResultAsync(string batchReference, CancellationToken ct = default);

    /// <summary>A page of the staged batch's rows (row number ascending), for the review table.</summary>
    Task<PagedResult<TransactionRowDto>> GetRecordsAsync(string batchReference, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Corrects a staged row and revalidates it in place (field rules, in-batch duplicates,
    /// already-reversed, and source-transaction checks). Only permitted before submission for review.</summary>
    Task<UploadBatchResultDto> EditRecordAsync(string batchReference, Guid transactionId, EditTransactionRowRequest request, CancellationToken ct = default);

    /// <summary>Removes a row from a staged batch. Only permitted before submission for review.</summary>
    Task<UploadBatchResultDto> DeleteRecordAsync(string batchReference, Guid transactionId, CancellationToken ct = default);

    /// <summary>Re-runs validation on a row's current values without changing them — useful after a
    /// conflicting row was deleted/edited elsewhere in the batch, or after a Transfer Service outage clears.</summary>
    Task<UploadBatchResultDto> RetryValidationAsync(string batchReference, Guid transactionId, CancellationToken ct = default);

    /// <summary>Explicit "Submit Records For Review" action: moves a Validated batch with at least
    /// one valid row into the Approvals queue (FR-09).</summary>
    Task<UploadBatchResultDto> SubmitForReviewAsync(string batchReference, CancellationToken ct = default);
}

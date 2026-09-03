using BulkReversal.Application.Features.Upload.Dtos;

namespace BulkReversal.Application.Features.Upload.Services;

public interface IBatchUploadService
{
    Task<UploadBatchResultDto> UploadAsync(UploadBatchCommand command, CancellationToken ct = default);

    /// <summary>Reconstructs the upload result (counts + invalid-row detail) for an already-staged
    /// batch, e.g. to regenerate the FR-08 error report without re-uploading.</summary>
    Task<UploadBatchResultDto> GetResultAsync(string batchReference, CancellationToken ct = default);
}

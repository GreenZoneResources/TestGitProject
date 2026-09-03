namespace BulkReversal.Application.Features.Upload.Dtos;

/// <summary>Command to stage and validate a new batch upload (FR-03). ASP.NET-free by design:
/// the API layer copies the incoming IFormFile into <see cref="FileStream"/> before invoking this.</summary>
public record UploadBatchCommand(string BatchName, Stream FileStream, string FileName, long FileSizeBytes);

public record InvalidRowDto(int RowNumber, string? SessionIdOrFtReference, IReadOnlyList<string> Errors);

public record UploadBatchResultDto(
    Guid BatchId,
    string BatchReference,
    string BatchName,
    int TotalRecords,
    int ValidRecords,
    int InvalidRecords,
    IReadOnlyList<InvalidRowDto> InvalidRows);

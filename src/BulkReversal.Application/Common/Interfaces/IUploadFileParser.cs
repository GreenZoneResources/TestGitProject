using BulkReversal.Application.Features.Upload.Dtos;

namespace BulkReversal.Application.Common.Interfaces;

/// <summary>Parses a Reversal Upload Template file (.csv or .xlsx, BRU-02) into raw, unvalidated rows.</summary>
public interface IUploadFileParser
{
    bool CanParse(string fileName);

    Task<IReadOnlyList<RawUploadRow>> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default);
}

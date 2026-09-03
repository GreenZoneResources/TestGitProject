using BulkReversal.Application.Features.StatusMonitoring.Dtos;
using BulkReversal.Application.Features.Upload.Dtos;

namespace BulkReversal.Application.Common.Interfaces;

/// <summary>Produces the downloadable artifacts referenced by FR-05, FR-08, and FR-17.</summary>
public interface IReportExportService
{
    /// <summary>The blank Reversal Upload Template, with headers matching BRD Section 6 (FR-05).</summary>
    byte[] BuildUploadTemplate();

    /// <summary>Per-row error report for a validated batch's invalid rows (FR-08).</summary>
    byte[] BuildInvalidRowsReport(UploadBatchResultDto result);

    /// <summary>Batch/transaction-level status export for reconciliation (FR-17).</summary>
    byte[] BuildStatusReport(IReadOnlyList<TransactionStatusDto> rows, ExportFormat format);
}

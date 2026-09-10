using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Features.StatusMonitoring.Dtos;

namespace BulkReversal.Application.Features.StatusMonitoring;

/// <summary>Backs the Dashboard and Status Monitoring screens (FR-15, FR-16, FR-17).</summary>
public interface IStatusMonitoringService
{
    Task<DashboardDto> GetDashboardAsync(int page = 1, int pageSize = 10, CancellationToken ct = default);

    Task<PagedResult<TransactionStatusDto>> SearchAsync(StatusFilter filter, CancellationToken ct = default);

    Task<byte[]> ExportAsync(StatusFilter filter, ExportFormat format, CancellationToken ct = default);
}

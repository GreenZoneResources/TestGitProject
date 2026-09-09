using Asp.Versioning;
using BulkReversal.API.Common;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Features.StatusMonitoring;
using BulkReversal.Application.Features.StatusMonitoring.Dtos;
using BulkReversal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>Dashboard and Status Monitoring screens (FR-15/FR-16/FR-17).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/status-monitoring")]
[Authorize(Roles = AppRoles.AnyUser)]
[Produces("application/json")]
public class StatusMonitoringController : ControllerBase
{
    private readonly IStatusMonitoringService _statusService;

    public StatusMonitoringController(IStatusMonitoringService statusService) => _statusService = statusService;

    /// <summary>Dashboard summary cards + recent batches (Dashboard screen).</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DashboardDto>> Dashboard([FromQuery] int recentBatchCount = 10, CancellationToken ct = default)
    {
        if (this.ValidateCount(nameof(recentBatchCount), recentBatchCount, min: 1, max: 100) is { } invalid)
            return invalid;

        var result = await _statusService.GetDashboardAsync(recentBatchCount, ct);
        return Ok(result);
    }

    /// <summary>FR-16: filterable transaction-level status view.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? batchReference,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] ReversalStatus? status,
        [FromQuery] TransactionType? transactionType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (this.ValidatePagination(page, pageSize) is { } invalidPage)
            return invalidPage;

        if (this.ValidateDateRange(fromDate, toDate) is { } invalidRange)
            return invalidRange;

        var filter = new StatusFilter(batchReference, fromDate, toDate, status, transactionType, page, pageSize);
        var result = await _statusService.SearchAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>FR-17: exportable Excel/PDF report of the filtered batch/transaction outcomes.</summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] string? batchReference,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] ReversalStatus? status,
        [FromQuery] TransactionType? transactionType,
        [FromQuery] ExportFormat format = ExportFormat.Excel,
        CancellationToken ct = default)
    {
        if (this.ValidateDateRange(fromDate, toDate) is { } invalidRange)
            return invalidRange;

        var filter = new StatusFilter(batchReference, fromDate, toDate, status, transactionType);
        var bytes = await _statusService.ExportAsync(filter, format, ct);

        var (contentType, extension) = format == ExportFormat.Pdf
            ? ("application/pdf", "pdf")
            : ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx");

        return File(bytes, contentType, $"reversal-status-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}");
    }
}

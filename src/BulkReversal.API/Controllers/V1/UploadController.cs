using Asp.Versioning;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Application.Features.Upload.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>Batch Upload screen (FR-03 to FR-08): upload, validate, and stage a Reversal Upload
/// Template, and download the blank template or an invalid-rows error report.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/upload")]
[Authorize(Roles = AppRoles.AnyUser)]
[Produces("application/json")]
public class UploadController : ControllerBase
{
    private readonly IBatchUploadService _uploadService;
    private readonly IReportExportService _exportService;

    public UploadController(IBatchUploadService uploadService, IReportExportService exportService)
    {
        _uploadService = uploadService;
        _exportService = exportService;
    }

    /// <summary>FR-05: downloadable copy of the current Reversal Upload Template.</summary>
    [HttpGet("template")]
    [AllowAnonymous] // Static, non-sensitive artifact; kept accessible without auth friction for convenience links.
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DownloadTemplate()
    {
        var bytes = _exportService.BuildUploadTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Reversal-Upload-Template.xlsx");
    }

    /// <summary>FR-03/FR-06/FR-07/FR-09: upload a batch file, validate every row, and stage valid
    /// records (auto-submitting them for approval when at least one row is valid).</summary>
    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UploadBatchResultDto>> Upload([FromForm] UploadFileRequest request, CancellationToken ct)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return ValidationProblem("The uploaded file is required and must not be empty.");
        }

        await using var stream = request.File.OpenReadStream();
        var command = new UploadBatchCommand(request.BatchName, stream, request.File.FileName, request.File.Length);
        var result = await _uploadService.UploadAsync(command, ct);

        return CreatedAtAction(nameof(Upload), new { batchReference = result.BatchReference }, result);
    }

    /// <summary>FR-08: error report for a batch's invalid rows, so Settlement can correct and
    /// re-upload only the failed records without resubmitting the whole file.</summary>
    [HttpGet("{batchReference}/invalid-rows-report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvalidRowsReport(string batchReference, CancellationToken ct)
    {
        var result = await _uploadService.GetResultAsync(batchReference, ct);
        var bytes = _exportService.BuildInvalidRowsReport(result);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{result.BatchReference}-invalid-rows.xlsx");
    }

    private ActionResult ValidationProblem(string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [message] }));
}

/// <summary>Multipart form-data payload for FR-03. A single bound complex type (rather than
/// separate [FromForm] scalar/file parameters) so Swashbuckle can generate a correct multipart
/// schema for Swagger UI's "Try it out".</summary>
public class UploadFileRequest
{
    public string BatchName { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}

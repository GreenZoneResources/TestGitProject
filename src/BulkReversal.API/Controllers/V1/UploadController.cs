using Asp.Versioning;
using BulkReversal.Application.Common.Constants;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Features.Upload.Dtos;
using BulkReversal.Application.Features.Upload.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Controllers.V1;

/// <summary>Batch Upload screen (FR-03 to FR-09): upload, validate, and stage a Reversal Upload
/// Template; review, edit, delete, and revalidate individual rows; then submit for approval.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/upload")]
[Authorize(Roles = AppRoles.AnyUser)]
[Produces("application/json")]
public class UploadController : ControllerBase
{
    private readonly IBatchUploadService _uploadService;
    private readonly IReportExportService _exportService;
    private readonly IValidator<EditTransactionRowRequest> _editValidator;

    public UploadController(
        IBatchUploadService uploadService, IReportExportService exportService, IValidator<EditTransactionRowRequest> editValidator)
    {
        _uploadService = uploadService;
        _exportService = exportService;
        _editValidator = editValidator;
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

    /// <summary>FR-03/FR-06/FR-07: upload a batch file and validate every row. The batch stays
    /// Validated (editable) until explicitly submitted via <see cref="Submit"/>.</summary>
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

        return CreatedAtAction(nameof(GetRecords), new { batchReference = result.BatchReference }, result);
    }

    /// <summary>Staged rows for the Upload results/review screen.</summary>
    [HttpGet("{batchReference}/records")]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TransactionRowDto>>> GetRecords(string batchReference, CancellationToken ct)
    {
        var records = await _uploadService.GetRecordsAsync(batchReference, ct);
        return Ok(records);
    }

    /// <summary>Corrects a single staged row and revalidates it in place.</summary>
    [HttpPut("{batchReference}/records/{transactionId:guid}")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> EditRecord(
        string batchReference, Guid transactionId, [FromBody] EditTransactionRowRequest request, CancellationToken ct)
    {
        var validation = await _editValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await _uploadService.EditRecordAsync(batchReference, transactionId, request, ct);
        return Ok(result);
    }

    /// <summary>Removes a single staged row from the batch.</summary>
    [HttpDelete("{batchReference}/records/{transactionId:guid}")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> DeleteRecord(string batchReference, Guid transactionId, CancellationToken ct)
    {
        var result = await _uploadService.DeleteRecordAsync(batchReference, transactionId, ct);
        return Ok(result);
    }

    /// <summary>Re-runs validation on a row's current values without changing them.</summary>
    [HttpPost("{batchReference}/records/{transactionId:guid}/retry")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> RetryRecord(string batchReference, Guid transactionId, CancellationToken ct)
    {
        var result = await _uploadService.RetryValidationAsync(batchReference, transactionId, ct);
        return Ok(result);
    }

    /// <summary>"Submit Records For Review": moves the batch into the Approvals queue (FR-09).</summary>
    [HttpPost("{batchReference}/submit")]
    [ProducesResponseType(typeof(UploadBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadBatchResultDto>> Submit(string batchReference, CancellationToken ct)
    {
        var result = await _uploadService.SubmitForReviewAsync(batchReference, ct);
        return Ok(result);
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
